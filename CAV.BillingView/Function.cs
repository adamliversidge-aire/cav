using System.Text.Json;
using Amazon.Billing;
using Amazon.Billing.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Organizations;
using Amazon.Organizations.Model;
using Amazon.RAM;
using Amazon.RAM.Model;
using CAV.BillingView.Extensions;
using CAV.BillingView.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CAV.BillingView;

public class Function
{
    private readonly IAmazonBilling _billingClient;
    private readonly IAmazonRAM _ramClient;
    private readonly IAmazonOrganizations _organizationsClient;
    
    public Function(IAmazonBilling billingClient,
        IAmazonRAM ramClient,
        IAmazonOrganizations organizationsClient)
    {
        _billingClient = billingClient;
        _ramClient = ramClient;
        _organizationsClient = organizationsClient;
    }

    /// <summary>
    /// Creates a billing view for a newly created production account and shares it with that account.
    /// </summary>
    /// <param name="event">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task FunctionHandler(CloudWatchEvent<ControlTowerEvent> @event,
        ILambdaContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation($"Processing event: {JsonSerializer.Serialize(@event)}");

        var account = GetAccountIdFromEvent(@event);

        if (IsProductionWorkload(account))
        {
            await CreateBillingViewForAccount(account!.Value, context, cancellationToken); //not null here
            return;
        }

        //todo: this could be DLT ?
        context.Logger.LogInformation("No account ID found in event, skipping.");
    }

    internal static bool IsProductionWorkload(AccountInfo? account) =>
        account.HasValue && account!.Value.AccountName.Contains("PROD");

    private async Task CreateBillingViewForAccount(AccountInfo account,
        ILambdaContext context,
        CancellationToken cancellationToken)
    {
        context.Logger.LogInformation($"Creating billing view for account {account.AccountId}");

        var billingViewName = $"billing-view-account-{account.AccountName}";

        if (await CheckIfExistsAsync(billingViewName, cancellationToken))
        {
            context.Logger.LogInformation($"Billing view {billingViewName} already exists, skipping.");
            return;
        }
        
        var accountService = account.AccountName.GetServiceFromAccountName();
        await CreateBillingAndShareAsync(billingViewName, accountService, account.AccountId, context, cancellationToken);
    }

    private async Task<bool> CheckIfExistsAsync(string billingViewName, CancellationToken cancellationToken)
    {
        var response = await _billingClient.ListBillingViewsAsync(new ListBillingViewsRequest
        {
            Names = [new StringSearch { SearchValue = billingViewName }],
        }, cancellationToken);

        return response.BillingViews.Any(x => x.Name == billingViewName);
    }

    private async Task<IReadOnlyCollection<string>> GetAccountsAsync(string service, CancellationToken cancellationToken)
    {
        var accounts = new List<Account>();
        string? nextToken = null;
        do
        {
            var response = await _organizationsClient.ListAccountsAsync(new ListAccountsRequest
            {
                NextToken = nextToken
            }, cancellationToken);

            accounts.AddRange(response.Accounts);
            nextToken = response.NextToken;
            
        } while (!string.IsNullOrEmpty(nextToken));

        return accounts
            .Where(x => x.Name.Contains($"{service}-"))
            .Select(x => x.Id)
            .ToList();
    }

    private async Task CreateBillingAndShareAsync(string billingViewName, string accountService, string accountId,
        ILambdaContext context, CancellationToken cancellationToken)
    {
        var linkedAccounts = await GetAccountsAsync(accountService, cancellationToken);

        var createViewResponse = await _billingClient.CreateBillingViewAsync(new CreateBillingViewRequest
        {
            Name = billingViewName,
            SourceViews = [$"arn:aws:billing::{context.InvokedFunctionArn.Split(':')[4]}:billingview/primary"],
            DataFilterExpression = new()
            {
                Dimensions = new()
                {
                    Key = "LINKED_ACCOUNT",
                    Values = linkedAccounts.ToList()
                }
            }
        }, cancellationToken);

        context.Logger.LogInformation($"Created billing view: {createViewResponse.Arn}");

        await ShareBillingViewWithAccount(createViewResponse.Arn, accountId, context, cancellationToken);
    }

    private async Task ShareBillingViewWithAccount(string viewArn, string accountId, ILambdaContext context,
        CancellationToken cancellationToken)
    {
        context.Logger.LogInformation($"Sharing billing view {viewArn} with account {accountId}");

        var shareResponse = await _ramClient.CreateResourceShareAsync(new CreateResourceShareRequest
        {
            Name = $"share-billing-view-{accountId}",
            ResourceArns = [viewArn],
            Principals = [accountId],
            AllowExternalPrincipals = false
        }, cancellationToken);

        context.Logger.LogInformation($"Created RAM share: {shareResponse.ResourceShare.ResourceShareArn}");
    }
    
    internal static AccountInfo? GetAccountIdFromEvent(CloudWatchEvent<ControlTowerEvent> @event)
    {
        var accountDetails = @event.Detail?.ServiceEventDetails?.CreateManagedAccountStatus;

        if (accountDetails is null || accountDetails.State != Constants.Success)
            return null;

        return accountDetails.Account;
    }
}
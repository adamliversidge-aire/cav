using System.Text.Json;
using Amazon.Billing;
using Amazon.Billing.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.RAM;
using Amazon.RAM.Model;
using CAV.BillingView.Models;
using Expression = System.Linq.Expressions.Expression;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CAV.BillingView;

public class Function
{
    private readonly IAmazonBilling _billingClient;
    private readonly IAmazonRAM _ramClient;
    
    public Function(IAmazonBilling billingClient, IAmazonRAM ramClient)
    {
        _billingClient = billingClient;
        _ramClient = ramClient;
    }
    
    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="input">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public async Task<Response> FunctionHandler(CloudWatchEvent<ControlTowerEvent> @event, ILambdaContext context)
    {
        context.Logger.LogInformation($"Processing event: {JsonSerializer.Serialize(@event)}");
        
        var account = GetAccountIdFromEvent(@event);

        if (IsProductionWorkload(account))
        {
            return await CreateBillingViewForAccount(account!.Value, context); //not null here
        }
        
        //todo: this could be DLT ?
        context.Logger.LogInformation("No account ID found in event, skipping.");
        return new Response
        {
        };
    }

    private static bool IsProductionWorkload(AccountInfo? account) =>
        account.HasValue || account!.Value.AccountName.Contains("PROD");

    private async Task<Response> CreateBillingViewForAccount(AccountInfo account, ILambdaContext context)
    {
        context.Logger.LogInformation($"Creating billing view for account {account.AccountId}");

        //todo: There could be a race condition here, depending on the order of the accounts that get created (i.e. dev / uat / prod)
        //We only want to setup the billing view to the prod account, however we need to assign all linked accounts for the service
        //Are we using the correct trigger, i.e. `account created` - should this be a periodic run ? 
        
        var createViewResponse = await _billingClient.CreateBillingViewAsync(new CreateBillingViewRequest
        {
            Name = $"billing-view-account-{account.AccountId}",
            SourceViews = [$"arn:aws:billing::{context.InvokedFunctionArn.Split(':')[4]}:billingview/primary"],
            DataFilterExpression =
            {
                Dimensions =
                {
                    Key = "LINKED_ACCOUNT",
                    Values = []
                }
            }
        });
        
        context.Logger.LogInformation($"Created billing view: {createViewResponse.Arn}");
        
        return await ShareBillingViewWithAccount(createViewResponse.Arn, account.AccountId, context);
    }
    
    private async Task<Response> ShareBillingViewWithAccount(string viewArn, string accountId, ILambdaContext context)
    {
        context.Logger.LogInformation($"Created billing view: {viewArn}");

        var shareResponse = await _ramClient.CreateResourceShareAsync(new CreateResourceShareRequest
        {
            Name = $"share-billing-view-{accountId}",
            ResourceArns = [viewArn],
            Principals = [accountId],
            AllowExternalPrincipals = false
        });

        context.Logger.LogInformation($"Created RAM share: {shareResponse.ResourceShare.ResourceShareArn}");

        return new Response
        {
            // Status = "ok",
            // AccountId = accountId,
            // BillingViewArn = viewArn,
            // ResourceShareArn = shareResponse.ResourceShare.ResourceShareArn
        };
    }
    
    private static AccountInfo? GetAccountIdFromEvent(CloudWatchEvent<ControlTowerEvent> @event)
    {
        var accountDetails = @event.Detail?.ServiceEventDetails?.CreateManagedAccountStatus;

        if (accountDetails is null || accountDetails.State != "SUCCEEDED")
            return null;

        return accountDetails.Account;
    }
}
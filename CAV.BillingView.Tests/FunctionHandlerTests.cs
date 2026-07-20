using Amazon.Billing;
using Amazon.Billing.Model;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Amazon.Organizations;
using Amazon.Organizations.Model;
using Amazon.RAM;
using Amazon.RAM.Model;
using CAV.BillingView.Models;
using NSubstitute;
using Xunit;

namespace CAV.BillingView.Tests;

public class FunctionHandlerTests
{
    private readonly IAmazonBilling _billingClient = Substitute.For<IAmazonBilling>();
    private readonly IAmazonRAM _ramClient = Substitute.For<IAmazonRAM>();
    private readonly IAmazonOrganizations _organizationsClient = Substitute.For<IAmazonOrganizations>();
    private readonly ILambdaContext _context = Substitute.For<ILambdaContext>();
    private readonly Function _function;

    public FunctionHandlerTests()
    {
        _context.Logger.Returns(Substitute.For<ILambdaLogger>());
        _context.InvokedFunctionArn.Returns("arn:aws:lambda:us-east-1:123456789012:function:billing-view");

        _organizationsClient.ListAccountsAsync(Arg.Any<ListAccountsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListAccountsResponse { Accounts = [], NextToken = null });

        _billingClient.CreateBillingViewAsync(Arg.Any<CreateBillingViewRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateBillingViewResponse { Arn = "arn:aws:billing::123456789012:billingview/new-view" });

        _ramClient.CreateResourceShareAsync(Arg.Any<CreateResourceShareRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateResourceShareResponse
            {
                ResourceShare = new ResourceShare { ResourceShareArn = "arn:aws:ram::123456789012:resource-share/abc" }
            });

        _function = new Function(_billingClient, _ramClient, _organizationsClient);
    }

    private static AccountInfo MakeAccount(string name, string id = "222233334444") =>
        new() { AccountName = name, AccountId = id };

    private static CloudWatchEvent<ControlTowerEvent> MakeEvent(AccountInfo account, string state = "SUCCEEDED") =>
        new()
        {
            Version = "0",
            Id = "event-id",
            DetailType = "AWS Service Event via CloudTrail",
            Source = "aws.controltower",
            Account = account.AccountId,
            Time = DateTime.UtcNow,
            Region = "us-east-1",
            Resources = [],
            Detail = new ControlTowerEvent
            {
                EventVersion = "1.08",
                EventTime = DateTime.UtcNow,
                EventSource = "organizations.amazonaws.com",
                EventName = "CreateManagedAccount",
                AwsRegion = "us-east-1",
                SourceIpAddress = "127.0.0.1",
                UserAgent = "aws-controltower",
                EventId = "detail-event-id",
                ReadOnly = false,
                EventType = "AwsServiceEvent",
                ManagementEvent = true,
                RecipientAccountId = account.AccountId,
                ServiceEventDetails = new ServiceEventDetails
                {
                    CreateManagedAccountStatus = new CreateManagedAccountStatus
                    {
                        OrganizationalUnit = new CAV.BillingView.Models.OrganizationalUnit(),
                        Account = account,
                        State = state,
                        Message = "message"
                    }
                }
            }
        };

    private void SetUpBillingViewExists(bool exists, string billingViewName) =>
        _billingClient.ListBillingViewsAsync(Arg.Any<ListBillingViewsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListBillingViewsResponse
            {
                BillingViews = exists
                    ? [new BillingViewListElement { Name = billingViewName }]
                    : []
            });

    [Fact]
    public async Task ProductionAccount_NewBillingView_CreatesViewAndSharesIt()
    {
        var account = MakeAccount("SERVICE-PROD");
        SetUpBillingViewExists(exists: false, billingViewName: $"billing-view-account-{account.AccountName}");

        await _function.FunctionHandler(MakeEvent(account), _context);

        await _billingClient.Received(1).CreateBillingViewAsync(
            Arg.Is<CreateBillingViewRequest>(r => r.Name == $"billing-view-account-{account.AccountName}"),
            Arg.Any<CancellationToken>());

        await _ramClient.Received(1).CreateResourceShareAsync(
            Arg.Is<CreateResourceShareRequest>(r => r.Principals.Contains(account.AccountId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProductionAccount_ExistingBillingView_DoesNotCreateOrShareAgain()
    {
        var account = MakeAccount("SERVICE-PROD");
        SetUpBillingViewExists(exists: true, billingViewName: $"billing-view-account-{account.AccountName}");

        await _function.FunctionHandler(MakeEvent(account), _context);

        await _billingClient.DidNotReceive().CreateBillingViewAsync(
            Arg.Any<CreateBillingViewRequest>(), Arg.Any<CancellationToken>());

        await _ramClient.DidNotReceive().CreateResourceShareAsync(
            Arg.Any<CreateResourceShareRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonProductionAccount_MakesNoBillingOrRamCalls()
    {
        var account = MakeAccount("SERVICE-DEV");

        await _function.FunctionHandler(MakeEvent(account), _context);

        await _billingClient.DidNotReceive().ListBillingViewsAsync(
            Arg.Any<ListBillingViewsRequest>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CreateBillingViewAsync(
            Arg.Any<CreateBillingViewRequest>(), Arg.Any<CancellationToken>());
        await _ramClient.DidNotReceive().CreateResourceShareAsync(
            Arg.Any<CreateResourceShareRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountCreationNotSucceeded_MakesNoBillingOrRamCalls()
    {
        var account = MakeAccount("SERVICE-PROD");

        await _function.FunctionHandler(MakeEvent(account, state: "IN_PROGRESS"), _context);

        await _billingClient.DidNotReceive().ListBillingViewsAsync(
            Arg.Any<ListBillingViewsRequest>(), Arg.Any<CancellationToken>());
        await _ramClient.DidNotReceive().CreateResourceShareAsync(
            Arg.Any<CreateResourceShareRequest>(), Arg.Any<CancellationToken>());
    }
}

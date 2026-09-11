using Amazon.Lambda.CloudWatchEvents;
using CAV.BillingView.Models;
using Xunit;

namespace CAV.BillingView.Tests;

public class FunctionPureLogicTests
{
    private static AccountInfo MakeAccount(string name, string id = "111122223333") =>
        new() { AccountName = name, AccountId = id };

    [Theory]
    [InlineData("SERVICE-PROD")]
    [InlineData("PROD-SERVICE")]
    [InlineData("SERVICE-PRODUCTION")]
    public void IsProductionWorkload_NameContainsProd_ReturnsTrue(string accountName)
    {
        var account = MakeAccount(accountName);

        Assert.True(Function.IsProductionWorkload(account));
    }

    [Theory]
    [InlineData("SERVICE-DEV")]
    [InlineData("SERVICE-STAGING")]
    [InlineData("SERVICE-TEST")]
    public void IsProductionWorkload_NameWithoutProd_ReturnsFalse(string accountName)
    {
        var account = MakeAccount(accountName);

        Assert.False(Function.IsProductionWorkload(account));
    }

    [Fact]
    public void IsProductionWorkload_NullAccount_ReturnsFalse()
    {
        Assert.False(Function.IsProductionWorkload(null));
    }

    private static CloudWatchEvent<ControlTowerEvent> MakeEvent(CreateManagedAccountStatus? status) =>
        new()
        {
            Version = "0",
            Id = "event-id",
            DetailType = "AWS Service Event via CloudTrail",
            Source = "aws.controltower",
            Account = "111122223333",
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
                RecipientAccountId = "111122223333",
                ServiceEventDetails = status is null
                    ? null
                    : new ServiceEventDetails { CreateManagedAccountStatus = status }
            }
        };

    [Fact]
    public void GetAccountIdFromEvent_Succeeded_ReturnsAccount()
    {
        var account = MakeAccount("SERVICE-PROD");
        var @event = MakeEvent(new CreateManagedAccountStatus
        {
            OrganizationalUnit = new CAV.BillingView.Models.OrganizationalUnit(),
            Account = account,
            State = "SUCCEEDED",
            Message = "account created"
        });

        var result = Function.GetAccountIdFromEvent(@event);

        Assert.Equal(account, result);
    }

    [Theory]
    [InlineData("IN_PROGRESS")]
    [InlineData("FAILED")]
    [InlineData("")]
    public void GetAccountIdFromEvent_NotSucceeded_ReturnsNull(string state)
    {
        var @event = MakeEvent(new CreateManagedAccountStatus
        {
            OrganizationalUnit = new CAV.BillingView.Models.OrganizationalUnit(),
            Account = MakeAccount("SERVICE-PROD"),
            State = state,
            Message = "in progress"
        });

        var result = Function.GetAccountIdFromEvent(@event);

        Assert.Null(result);
    }

    [Fact]
    public void GetAccountIdFromEvent_MissingServiceEventDetails_ReturnsNull()
    {
        var @event = MakeEvent(null);

        var result = Function.GetAccountIdFromEvent(@event);

        Assert.Null(result);
    }
}

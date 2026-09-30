using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using CAV.EntraLeavers.Configuration;
using CAV.EntraLeavers.Entra;
using CAV.EntraLeavers.Entra.Models;
using CAV.EntraLeavers.Models;
using NSubstitute;
using StatusType = Amazon.IdentityManagement.StatusType;
using Tag = Amazon.IdentityManagement.Model.Tag;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace CAV.EntraLeavers.Tests;

public class FunctionHandlerTests
{
    private const string TopicArn = "arn:aws:sns:eu-west-2:123456789012:team";
    private const string SecretArn = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:entra";

    private readonly IAmazonIdentityManagementService _iamClient = Substitute.For<IAmazonIdentityManagementService>();
    private readonly IAmazonSimpleNotificationService _snsClient = Substitute.For<IAmazonSimpleNotificationService>();
    private readonly IAmazonSecretsManager _secretsClient = Substitute.For<IAmazonSecretsManager>();
    private readonly IEntraGraphClient _graphClient = Substitute.For<IEntraGraphClient>();
    private readonly ILambdaContext _context = Substitute.For<ILambdaContext>();
    private readonly EventBridgeEvent<object> _event = new();

    public FunctionHandlerTests()
    {
        _context.Logger.Returns(Substitute.For<ILambdaLogger>());
        _context.InvokedFunctionArn.Returns("arn:aws:lambda:eu-west-2:123456789012:function:cav-entra-leavers");

        _secretsClient.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse
            {
                SecretString = """{"tenantId":"t","clientId":"c","clientSecret":"s"}"""
            });

        _iamClient.ListUserTagsAsync(Arg.Any<ListUserTagsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListUserTagsResponse { Tags = [] });
        _iamClient.GetLoginProfileAsync(Arg.Any<GetLoginProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetLoginProfileResponse());
        _iamClient.ListAccessKeysAsync(Arg.Any<ListAccessKeysRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListAccessKeysResponse { AccessKeyMetadata = [] });
    }

    private Function MakeFunction(bool dryRun, int maxDisablePerRun = 25) =>
        new(_iamClient, _snsClient, _secretsClient, _graphClient, new LeaversOptions
        {
            InactiveDays = 90,
            DryRun = dryRun,
            MaxDisablePerRun = maxDisablePerRun,
            SnsTopicArn = TopicArn,
            EntraSecretArn = SecretArn
        });

    private static GraphUser StaleUser(string email) => new()
    {
        Mail = email,
        UserPrincipalName = email,
        DisplayName = email,
        SignInActivity = new SignInActivity { LastSignInDateTime = DateTimeOffset.UtcNow.AddDays(-200) }
    };

    private void GivenEntraUsers(params GraphUser[] users) =>
        _graphClient.GetUsersInactiveSinceAsync(Arg.Any<EntraCredentials>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(users);

    private void GivenIamUsers(params string[] names) =>
        _iamClient.ListUsersAsync(Arg.Any<ListUsersRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListUsersResponse { Users = names.Select(n => new User { UserName = n }).ToList() });

    private void GivenAccessKeys(string user, params (string Id, StatusType Status)[] keys) =>
        _iamClient.ListAccessKeysAsync(Arg.Is<ListAccessKeysRequest>(r => r.UserName == user), Arg.Any<CancellationToken>())
            .Returns(new ListAccessKeysResponse
            {
                AccessKeyMetadata = keys.Select(k => new AccessKeyMetadata
                {
                    UserName = user,
                    AccessKeyId = k.Id,
                    Status = k.Status
                }).ToList()
            });

    private async Task AssertNoIamChanges()
    {
        await _iamClient.DidNotReceive().DeleteLoginProfileAsync(Arg.Any<DeleteLoginProfileRequest>(), Arg.Any<CancellationToken>());
        await _iamClient.DidNotReceive().UpdateAccessKeyAsync(Arg.Any<UpdateAccessKeyRequest>(), Arg.Any<CancellationToken>());
        await _iamClient.DidNotReceive().TagUserAsync(Arg.Any<TagUserRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoStaleEntraUsers_DoesNothing()
    {
        GivenEntraUsers();

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _iamClient.DidNotReceive().ListUsersAsync(Arg.Any<ListUsersRequest>(), Arg.Any<CancellationToken>());
        await _snsClient.DidNotReceive().PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntraSecret_IsReadFromConfiguredArn()
    {
        GivenEntraUsers();

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _secretsClient.Received(1).GetSecretValueAsync(
            Arg.Is<GetSecretValueRequest>(r => r.SecretId == SecretArn), Arg.Any<CancellationToken>());
        await _graphClient.Received(1).GetUsersInactiveSinceAsync(
            new EntraCredentials("t", "c", "s"), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DryRun_MatchedUser_ReportsWithoutChangingIam()
    {
        GivenEntraUsers(StaleUser("alice@example.com"));
        GivenIamUsers("alice@example.com", "bob@example.com");
        GivenAccessKeys("alice@example.com", ("AKIA0001", StatusType.Active));

        await MakeFunction(dryRun: true).FunctionHandler(_event, _context);

        await AssertNoIamChanges();
        await _snsClient.Received(1).PublishAsync(
            Arg.Is<PublishRequest>(r => r.TopicArn == TopicArn
                                        && r.Subject.StartsWith("[DRY RUN]")
                                        && r.Message.Contains("alice@example.com")
                                        && !r.Message.Contains("bob@example.com")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiveRun_MatchedUser_RemovesConsoleDeactivatesActiveKeysAndTags()
    {
        GivenEntraUsers(StaleUser("alice@example.com"));
        GivenIamUsers("alice@example.com");
        GivenAccessKeys("alice@example.com", ("AKIA_INACTIVE", StatusType.Inactive), ("AKIA_ACTIVE", StatusType.Active));

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _iamClient.Received(1).DeleteLoginProfileAsync(
            Arg.Is<DeleteLoginProfileRequest>(r => r.UserName == "alice@example.com"), Arg.Any<CancellationToken>());
        await _iamClient.Received(1).UpdateAccessKeyAsync(
            Arg.Is<UpdateAccessKeyRequest>(r => r.AccessKeyId == "AKIA_ACTIVE" && r.Status == StatusType.Inactive),
            Arg.Any<CancellationToken>());
        await _iamClient.DidNotReceive().UpdateAccessKeyAsync(
            Arg.Is<UpdateAccessKeyRequest>(r => r.AccessKeyId == "AKIA_INACTIVE"), Arg.Any<CancellationToken>());
        await _iamClient.Received(1).TagUserAsync(
            Arg.Is<TagUserRequest>(r => r.Tags.Any(t => t.Key == "DisabledBy" && t.Value == "cav-entra-leavers")),
            Arg.Any<CancellationToken>());
        await _snsClient.Received(1).PublishAsync(
            Arg.Is<PublishRequest>(r => !r.Subject.Contains("DRY RUN") && r.Message.Contains("console password removed")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiveRun_MatchOnEmailTag_DisablesTaggedUser()
    {
        GivenEntraUsers(StaleUser("alice@example.com"));
        GivenIamUsers("alice");
        _iamClient.ListUserTagsAsync(Arg.Is<ListUserTagsRequest>(r => r.UserName == "alice"), Arg.Any<CancellationToken>())
            .Returns(new ListUserTagsResponse { Tags = [new Tag { Key = "Email", Value = "alice@example.com" }] });

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _iamClient.Received(1).DeleteLoginProfileAsync(
            Arg.Is<DeleteLoginProfileRequest>(r => r.UserName == "alice"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserMatchedByName_DoesNotFetchTags()
    {
        GivenEntraUsers(StaleUser("alice@example.com"));
        GivenIamUsers("alice@example.com");

        await MakeFunction(dryRun: true).FunctionHandler(_event, _context);

        await _iamClient.DidNotReceive().ListUserTagsAsync(Arg.Any<ListUserTagsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IamUsers_AreReadAcrossPages()
    {
        GivenEntraUsers(StaleUser("bob@example.com"));
        _iamClient.ListUsersAsync(Arg.Is<ListUsersRequest>(r => r.Marker == null), Arg.Any<CancellationToken>())
            .Returns(new ListUsersResponse { Users = [new User { UserName = "alice@example.com" }], IsTruncated = true, Marker = "page2" });
        _iamClient.ListUsersAsync(Arg.Is<ListUsersRequest>(r => r.Marker == "page2"), Arg.Any<CancellationToken>())
            .Returns(new ListUsersResponse { Users = [new User { UserName = "bob@example.com" }], IsTruncated = false });

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _iamClient.Received(1).DeleteLoginProfileAsync(
            Arg.Is<DeleteLoginProfileRequest>(r => r.UserName == "bob@example.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserWithNoConsoleOrActiveKeys_IsNotReportedOrTagged()
    {
        GivenEntraUsers(StaleUser("alice@example.com"));
        GivenIamUsers("alice@example.com");
        _iamClient.GetLoginProfileAsync(Arg.Any<GetLoginProfileRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NoSuchEntityException("no login profile"));

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await AssertNoIamChanges();
        await _snsClient.DidNotReceive().PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneUserFails_OthersStillDisabledAndFailureReported()
    {
        GivenEntraUsers(StaleUser("alice@example.com"), StaleUser("bob@example.com"));
        GivenIamUsers("alice@example.com", "bob@example.com");
        _iamClient.DeleteLoginProfileAsync(Arg.Is<DeleteLoginProfileRequest>(r => r.UserName == "alice@example.com"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonIdentityManagementServiceException("AccessDenied"));

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _iamClient.Received(1).DeleteLoginProfileAsync(
            Arg.Is<DeleteLoginProfileRequest>(r => r.UserName == "bob@example.com"), Arg.Any<CancellationToken>());
        await _snsClient.Received(1).PublishAsync(
            Arg.Is<PublishRequest>(r => r.Subject.Contains("1 failed") && r.Message.Contains("AccessDenied")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiveRun_OverMaxDisableLimit_AbortsAndAlerts()
    {
        GivenEntraUsers(StaleUser("a@example.com"), StaleUser("b@example.com"), StaleUser("c@example.com"));
        GivenIamUsers("a@example.com", "b@example.com", "c@example.com");

        await MakeFunction(dryRun: false, maxDisablePerRun: 2).FunctionHandler(_event, _context);

        await AssertNoIamChanges();
        await _snsClient.Received(1).PublishAsync(
            Arg.Is<PublishRequest>(r => r.Subject.Contains("ABORTED")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntraUserActiveViaNonInteractiveSignIn_IsNotDisabled()
    {
        var user = StaleUser("alice@example.com");
        user.SignInActivity!.LastNonInteractiveSignInDateTime = DateTimeOffset.UtcNow.AddDays(-1);
        GivenEntraUsers(user);
        GivenIamUsers("alice@example.com");

        await MakeFunction(dryRun: false).FunctionHandler(_event, _context);

        await _iamClient.DidNotReceive().ListUsersAsync(Arg.Any<ListUsersRequest>(), Arg.Any<CancellationToken>());
        await AssertNoIamChanges();
    }
}

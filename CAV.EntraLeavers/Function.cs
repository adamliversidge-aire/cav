using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Lambda.Annotations;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using CAV.EntraLeavers.Configuration;
using CAV.EntraLeavers.Entra;
using CAV.EntraLeavers.Entra.Models;
using CAV.EntraLeavers.Iam;
using CAV.EntraLeavers.Matching;
using CAV.EntraLeavers.Models;
using CAV.EntraLeavers.Notifications;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CAV.EntraLeavers;

/// <summary>
/// Finds Entra ID users who haven't signed in for INACTIVE_DAYS, matches them to IAM users by
/// email, disables those IAM users (console password + access keys) and emails the team via SNS.
/// </summary>
public class Function(
    IAmazonIdentityManagementService iamClient,
    IAmazonSimpleNotificationService simpleNotificationService,
    IAmazonSecretsManager secretsManagerClient,
    IEntraGraphClient entraGraphClient,
    LeaversOptions options)
{
    private readonly IamUserDisabler _disabler = new(iamClient);

    [LambdaFunction(MemorySize = 512, Timeout = 120)]
    public async Task FunctionHandler(EventBridgeEvent<object> input, ILambdaContext context)
    {
        var logger = context.Logger;
        var now = DateTimeOffset.UtcNow;
        var run = new RunContext(
            GetAccountId(context),
            options.InactiveDays,
            now.AddDays(-options.InactiveDays),
            options.DryRun);

        logger.LogInformation(
            $"Starting Entra leavers run: inactiveDays={run.InactiveDays}, cutoff={run.Cutoff:o}, dryRun={run.DryRun}");

        var credentials = await GetEntraCredentialsAsync();
        var entraUsers = await entraGraphClient.GetUsersInactiveSinceAsync(credentials, run.Cutoff);
        var staleUsers = entraUsers.Where(u => u.IsInactiveSince(run.Cutoff)).ToList();

        logger.LogInformation(
            $"Graph returned {entraUsers.Count} user(s); {staleUsers.Count} inactive across all sign-in types.");

        if (staleUsers.Count == 0)
        {
            return;
        }

        var emailIndex = IamUserMatcher.BuildEmailIndex(staleUsers);
        var iamIdentities = await GetIamIdentitiesAsync(emailIndex);
        var matches = IamUserMatcher.Match(emailIndex, iamIdentities);

        logger.LogInformation($"Matched {matches.Count} of {iamIdentities.Count} IAM user(s) to stale Entra users.");

        if (!run.DryRun && matches.Count > options.MaxDisablePerRun)
        {
            logger.LogWarning(
                $"Aborting: {matches.Count} matches exceeds MAX_DISABLE_PER_RUN={options.MaxDisablePerRun}.");
            await PublishAsync(SummaryBuilder.BuildLimitExceeded(run, matches.Count, options.MaxDisablePerRun));
            return;
        }

        List<LeaverOutcome> outcomes = [];
        foreach (var match in matches)
        {
            DisableResult result;
            try
            {
                result = await _disabler.DisableAsync(match.IamUserName, run.DryRun, now.UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep going so one bad user doesn't stop the rest from being disabled.
                logger.LogError($"Failed to disable {match.IamUserName}: {ex.Message}");
                result = new DisableResult(match.IamUserName, false, [], ex.Message);
            }

            logger.LogInformation(
                $"{match.IamUserName}: consoleRemoved={result.ConsoleAccessRemoved}, " +
                $"keysDeactivated={result.AccessKeysDeactivated.Count}, dryRun={run.DryRun}");
            outcomes.Add(new LeaverOutcome(match, result));
        }

        var notification = SummaryBuilder.Build(run, outcomes);
        if (notification is null)
        {
            logger.LogInformation("All matched users were already disabled; no notification sent.");
            return;
        }

        await PublishAsync(notification);
    }

    private async Task<EntraCredentials> GetEntraCredentialsAsync()
    {
        var secret = await secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = options.EntraSecretArn
        });

        return EntraCredentials.FromJson(secret.SecretString);
    }

    /// <summary>
    /// Lists every IAM user. Tags are only fetched for users whose name isn't already a stale
    /// Entra email, which keeps ListUserTags calls to the users that need them.
    /// </summary>
    private async Task<List<IamIdentity>> GetIamIdentitiesAsync(IReadOnlyDictionary<string, GraphUser> emailIndex)
    {
        List<IamIdentity> identities = [];
        string? marker = null;

        do
        {
            var response = await iamClient.ListUsersAsync(new ListUsersRequest { Marker = marker });

            foreach (var user in response.Users ?? [])
            {
                var emailTag = emailIndex.ContainsKey(user.UserName)
                    ? null
                    : await GetEmailTagAsync(user.UserName);
                identities.Add(new IamIdentity(user.UserName, emailTag));
            }

            marker = response.IsTruncated == true ? response.Marker : null;
        } while (marker is not null);

        return identities;
    }

    private async Task<string?> GetEmailTagAsync(string userName)
    {
        string? marker = null;

        do
        {
            var response = await iamClient.ListUserTagsAsync(new ListUserTagsRequest
            {
                UserName = userName,
                Marker = marker
            });

            var tag = (response.Tags ?? []).FirstOrDefault(t =>
                string.Equals(t.Key, options.IamEmailTagKey, StringComparison.OrdinalIgnoreCase));
            if (tag is not null)
            {
                return tag.Value;
            }

            marker = response.IsTruncated == true ? response.Marker : null;
        } while (marker is not null);

        return null;
    }

    private async Task PublishAsync(Notification notification) =>
        await simpleNotificationService.PublishAsync(new PublishRequest
        {
            TopicArn = options.SnsTopicArn,
            Subject = notification.Subject,
            Message = notification.Message
        });

    private static string GetAccountId(ILambdaContext context)
    {
        var parts = context.InvokedFunctionArn?.Split(':');
        return parts is { Length: > 4 } ? parts[4] : "unknown";
    }
}

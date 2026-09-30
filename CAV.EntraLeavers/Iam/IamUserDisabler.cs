using System.Globalization;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;

namespace CAV.EntraLeavers.Iam;

public sealed record DisableResult(
    string UserName,
    bool ConsoleAccessRemoved,
    IReadOnlyList<string> AccessKeysDeactivated,
    string? Error = null)
{
    public bool Changed => ConsoleAccessRemoved || AccessKeysDeactivated.Count > 0;
}

/// <summary>
/// Disables an IAM user reversibly: removes the console password and marks every active
/// access key Inactive. The user, its policies and its keys are kept so it can be re-enabled.
/// </summary>
public sealed class IamUserDisabler(IAmazonIdentityManagementService iamClient)
{
    internal const string DisabledByTagValue = "cav-entra-leavers";

    /// <summary>
    /// Works out what needs disabling and, unless <paramref name="dryRun"/> is set, does it.
    /// The result describes the changes made (or that would be made in a dry run).
    /// </summary>
    public async Task<DisableResult> DisableAsync(
        string userName,
        bool dryRun,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var hasConsoleAccess = await HasLoginProfileAsync(userName, cancellationToken);
        var activeKeyIds = await GetActiveAccessKeyIdsAsync(userName, cancellationToken);
        var result = new DisableResult(userName, hasConsoleAccess, activeKeyIds);

        if (dryRun || !result.Changed)
        {
            return result;
        }

        if (hasConsoleAccess)
        {
            try
            {
                await iamClient.DeleteLoginProfileAsync(
                    new DeleteLoginProfileRequest { UserName = userName }, cancellationToken);
            }
            catch (NoSuchEntityException)
            {
                // Removed between the check and the delete; nothing left to do.
            }
        }

        foreach (var keyId in activeKeyIds)
        {
            await iamClient.UpdateAccessKeyAsync(new UpdateAccessKeyRequest
            {
                UserName = userName,
                AccessKeyId = keyId,
                Status = StatusType.Inactive
            }, cancellationToken);
        }

        await iamClient.TagUserAsync(new TagUserRequest
        {
            UserName = userName,
            Tags =
            [
                new Tag { Key = "DisabledBy", Value = DisabledByTagValue },
                new Tag { Key = "DisabledOn", Value = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            ]
        }, cancellationToken);

        return result;
    }

    private async Task<bool> HasLoginProfileAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            await iamClient.GetLoginProfileAsync(
                new GetLoginProfileRequest { UserName = userName }, cancellationToken);
            return true;
        }
        catch (NoSuchEntityException)
        {
            return false;
        }
    }

    private async Task<List<string>> GetActiveAccessKeyIdsAsync(string userName, CancellationToken cancellationToken)
    {
        var response = await iamClient.ListAccessKeysAsync(
            new ListAccessKeysRequest { UserName = userName }, cancellationToken);

        // Match on the key's own status, not on its position in the list.
        return (response.AccessKeyMetadata ?? [])
            .Where(k => k.Status == StatusType.Active)
            .Select(k => k.AccessKeyId)
            .ToList();
    }
}

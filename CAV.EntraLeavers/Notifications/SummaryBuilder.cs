using System.Globalization;
using System.Text;
using CAV.EntraLeavers.Iam;
using CAV.EntraLeavers.Matching;

namespace CAV.EntraLeavers.Notifications;

public sealed record LeaverOutcome(MatchedUser Match, DisableResult Result);

public sealed record Notification(string Subject, string Message);

public sealed record RunContext(string AccountId, int InactiveDays, DateTimeOffset Cutoff, bool DryRun);

public static class SummaryBuilder
{
    private const string SubjectPrefix = "CAV Entra leavers";
    private const string DryRunPrefix = "[DRY RUN] ";

    /// <summary>
    /// Builds the team email. Users that needed no change (already disabled) are left out,
    /// so a daily run only reports new work. Returns null when there is nothing to report.
    /// </summary>
    public static Notification? Build(RunContext run, IReadOnlyCollection<LeaverOutcome> outcomes)
    {
        var changed = outcomes.Where(o => o.Result.Error is null && o.Result.Changed).ToList();
        var failed = outcomes.Where(o => o.Result.Error is not null).ToList();

        if (changed.Count == 0 && failed.Count == 0)
        {
            return null;
        }

        var verb = run.DryRun ? "to disable" : "disabled";
        var subject = $"{(run.DryRun ? DryRunPrefix : "")}{SubjectPrefix}: {changed.Count} IAM user(s) {verb}";
        if (failed.Count > 0)
        {
            subject += $", {failed.Count} failed";
        }

        var message = new StringBuilder();
        AppendHeader(message, run);

        if (changed.Count > 0)
        {
            message.AppendLine(run.DryRun
                ? $"IAM users that WOULD be disabled ({changed.Count}):"
                : $"IAM users disabled ({changed.Count}):");

            foreach (var outcome in changed)
            {
                AppendUser(message, outcome);
                message.AppendLine($"    Action: {DescribeActions(outcome.Result)}");
            }

            message.AppendLine();
        }

        if (failed.Count > 0)
        {
            message.AppendLine($"Failures ({failed.Count}):");
            foreach (var outcome in failed)
            {
                AppendUser(message, outcome);
                message.AppendLine($"    Error: {outcome.Result.Error}");
            }

            message.AppendLine();
        }

        AppendFooter(message);

        return new Notification(subject, message.ToString());
    }

    /// <summary>Sent instead of disabling anyone when a live run exceeds MAX_DISABLE_PER_RUN.</summary>
    public static Notification BuildLimitExceeded(RunContext run, int matchedCount, int maxDisablePerRun)
    {
        var message = new StringBuilder();
        AppendHeader(message, run);
        message.AppendLine(
            $"{matchedCount} IAM users matched stale Entra accounts, which is more than the " +
            $"MAX_DISABLE_PER_RUN limit of {maxDisablePerRun}. No users were disabled.");
        message.AppendLine();
        message.AppendLine(
            "Check the Entra sign-in data and the lambda logs. If the number is expected, raise " +
            "max_disable_per_run in Terraform, or run once with dry_run = true to review the list.");
        message.AppendLine();
        AppendFooter(message);

        return new Notification($"{SubjectPrefix}: ABORTED - {matchedCount} users over limit", message.ToString());
    }

    internal static string DescribeActions(DisableResult result)
    {
        List<string> actions = [];

        if (result.ConsoleAccessRemoved)
        {
            actions.Add("console password removed");
        }

        if (result.AccessKeysDeactivated.Count > 0)
        {
            var keys = string.Join(", ", result.AccessKeysDeactivated.Select(MaskKeyId));
            actions.Add($"access key(s) deactivated: {keys}");
        }

        return string.Join("; ", actions);
    }

    internal static string MaskKeyId(string keyId) =>
        keyId.Length <= 4 ? keyId : $"****{keyId[^4..]}";

    private static void AppendHeader(StringBuilder message, RunContext run)
    {
        message.AppendLine($"AWS account: {run.AccountId}");
        message.AppendLine(
            $"Inactivity threshold: {run.InactiveDays} days (no Entra sign-in since {FormatDate(run.Cutoff)})");
        message.AppendLine(run.DryRun
            ? "Mode: DRY RUN - no changes were made."
            : "Mode: LIVE");
        message.AppendLine();
    }

    private static void AppendUser(StringBuilder message, LeaverOutcome outcome)
    {
        var entra = outcome.Match.EntraUser;
        var matchedOn = outcome.Match.Source == MatchSource.UserName ? "user name" : "email tag";
        var email = entra.Mail ?? entra.UserPrincipalName ?? "(no email)";
        var lastSignIn = entra.LastActivity is { } last ? FormatDate(last) : "never";

        message.AppendLine($"  - {outcome.Match.IamUserName} (matched on {matchedOn})");
        message.AppendLine($"    Entra: {entra.DisplayName ?? "(no name)"} <{email}>, last sign-in {lastSignIn}");
    }

    private static void AppendFooter(StringBuilder message)
    {
        message.AppendLine(
            "Disabled users keep their policies and keys. To re-enable, create a new login profile " +
            "and/or set the access key back to Active, then remove the DisabledBy/DisabledOn tags.");
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

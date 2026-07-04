using CAV.AccountUsage.Models;

namespace CAV.AccountUsage.Filtering;

public static class CredentialReportFilters
{
    /// <summary>
    /// Returns IAM users whose console password is enabled but who have not logged in
    /// within <paramref name="cutoff"/>. Users whose <c>PasswordLastUsed</c> is a sentinel
    /// value ("N/A", "no_information") are treated as never having logged in and are included.
    /// </summary>
    public static List<UserReportLineItem> GetInactiveConsoleUsers(
        IReadOnlyCollection<UserReportLineItem> report,
        IEnumerable<string> iamUserNames,
        DateTime cutoff)
    {
        var userNameSet = iamUserNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return report
            .Where(r =>
                userNameSet.Contains(r.User ?? string.Empty) &&
                r.PasswordEnabled == "true" &&
                (!TryParseAwsDate(r.PasswordLastUsed, out var lastUsed) || lastUsed < cutoff))
            .ToList();
    }

    /// <summary>
    /// Attempts to parse an AWS credential-report date string.
    /// Returns <c>false</c> for <c>null</c>, empty, or sentinel values
    /// ("N/A", "no_information", "not_supported").
    /// </summary>
    public static bool TryParseAwsDate(string? value, out DateTime result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
               && value != "N/A"
               && value != "no_information"
               && value != "not_supported"
               && DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out result);
    }
    
    /// <summary>
    /// Returns IAM users who have at least one <b>active</b> access key that has not been
    /// used since <paramref name="cutoff"/>. Keys whose last-used date is a sentinel value
    /// ("N/A") are treated as never used and are included.
    /// </summary>
    public static List<UserReportLineItem> GetUsersWithStaleAccessKeys(
        IReadOnlyCollection<UserReportLineItem> report,
        IEnumerable<string> iamUserNames,
        DateTime cutoff)
    {
        var userNameSet = iamUserNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return report
            .Where(r =>
                userNameSet.Contains(r.User ?? string.Empty) &&
                (IsStaleKey(r.AccessKey1Active, r.AccessKey1LastUsedDate, cutoff) ||
                 IsStaleKey(r.AccessKey2Active, r.AccessKey2LastUsedDate, cutoff)))
            .ToList();
    }

    /// <summary>
    /// Returns <c>true</c> when the key is active and has not been used since <paramref name="cutoff"/>.
    /// </summary>
    private static bool IsStaleKey(bool active, string? lastUsedDate, DateTime cutoff) =>
        active && (!TryParseAwsDate(lastUsedDate, out var lastUsed) || lastUsed < cutoff);
}

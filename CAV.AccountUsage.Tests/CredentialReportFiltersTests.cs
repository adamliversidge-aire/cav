using CAV.AccountUsage.Filtering;
using CAV.AccountUsage.Models;
using Xunit;

namespace CAV.AccountUsage.Tests;

public class CredentialReportFiltersTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static readonly DateTime Cutoff = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static UserReportLineItem MakeUser(
        string user,
        string passwordEnabled = "true",
        string? passwordLastUsed = null) =>
        new()
        {
            User = user,
            Arn = $"arn:aws:iam::123456789012:user/{user}",
            PasswordEnabled = passwordEnabled,
            PasswordLastUsed = passwordLastUsed
        };

    private static List<string> IamNames(params string[] names) => [.. names];

    // -----------------------------------------------------------------------
    // GetInactiveConsoleUsers
    // -----------------------------------------------------------------------

    [Fact]
    public void ActiveUser_LoggedInRecently_IsExcluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: "2025-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void ActiveUser_LoggedInExactlyAtCutoff_IsExcluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: "2025-01-01T00:00:00Z") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void ActiveUser_LoggedInBefore_Cutoff_IsIncluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
        Assert.Equal("alice", result[0].User);
    }

    [Fact]
    public void ActiveUser_PasswordLastUsed_NotApplicable_IsIncluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: "N/A") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void ActiveUser_PasswordLastUsed_NoInformation_IsIncluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: "no_information") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void ActiveUser_PasswordLastUsed_Null_IsIncluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: null) };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void PasswordDisabledUser_IsExcluded()
    {
        var report = new[] { MakeUser("alice", passwordEnabled: "false", passwordLastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void NotSupportedPasswordEnabled_IsExcluded()
    {
        // Root account row has password_enabled = "not_supported"
        var report = new[] { MakeUser("<root_account>", passwordEnabled: "not_supported", passwordLastUsed: "N/A") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("<root_account>"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void UserNotInIamList_IsExcluded()
    {
        var report = new[] { MakeUser("alice", passwordLastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("bob"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void IamNameMatching_IsCaseInsensitive()
    {
        var report = new[] { MakeUser("Alice", passwordLastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void MixedReport_ReturnsOnlyInactiveEnabledIamUsers()
    {
        var report = new[]
        {
            MakeUser("inactive",    passwordEnabled: "true",  passwordLastUsed: "2024-06-01T00:00:00Z"), // included
            MakeUser("recent",      passwordEnabled: "true",  passwordLastUsed: "2025-06-01T00:00:00Z"), // excluded – logged in recently
            MakeUser("disabled",    passwordEnabled: "false", passwordLastUsed: "2024-06-01T00:00:00Z"), // excluded – no console access
            MakeUser("never",       passwordEnabled: "true",  passwordLastUsed: "N/A"),                  // included – never logged in
            MakeUser("notInIam",    passwordEnabled: "true",  passwordLastUsed: "2024-06-01T00:00:00Z"), // excluded – not in IAM list
        };

        var result = CredentialReportFilters.GetInactiveConsoleUsers(
            report,
            IamNames("inactive", "recent", "disabled", "never"),
            Cutoff);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.User == "inactive");
        Assert.Contains(result, r => r.User == "never");
    }

    // -----------------------------------------------------------------------
    // GetUsersWithStaleAccessKeys
    // -----------------------------------------------------------------------

    private static UserReportLineItem MakeKeyUser(
        string user,
        bool key1Active = false, string? key1LastUsed = null,
        bool key2Active = false, string? key2LastUsed = null) =>
        new()
        {
            User = user,
            Arn = $"arn:aws:iam::123456789012:user/{user}",
            AccessKey1Active = key1Active,
            AccessKey1LastUsedDate = key1LastUsed,
            AccessKey2Active = key2Active,
            AccessKey2LastUsedDate = key2LastUsed,
        };

    [Fact]
    public void Key1_Active_NeverUsed_IsIncluded()
    {
        var report = new[] { MakeKeyUser("alice", key1Active: true, key1LastUsed: "N/A") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void Key1_Active_LastUsedBeforeCutoff_IsIncluded()
    {
        var report = new[] { MakeKeyUser("alice", key1Active: true, key1LastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void Key1_Active_LastUsedAfterCutoff_IsExcluded()
    {
        var report = new[] { MakeKeyUser("alice", key1Active: true, key1LastUsed: "2025-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void Key1_Inactive_IsExcluded()
    {
        var report = new[] { MakeKeyUser("alice", key1Active: false, key1LastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void Key2_Active_NeverUsed_IsIncluded()
    {
        var report = new[] { MakeKeyUser("alice", key2Active: true, key2LastUsed: "N/A") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void Key2_Active_LastUsedBeforeCutoff_IsIncluded()
    {
        var report = new[] { MakeKeyUser("alice", key2Active: true, key2LastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void Key2_Inactive_IsExcluded()
    {
        var report = new[] { MakeKeyUser("alice", key2Active: false, key2LastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void BothKeys_OneStaleOneRecent_IsIncluded()
    {
        // Key 1 stale, key 2 used recently — user still flagged because key 1 is stale
        var report = new[] { MakeKeyUser("alice",
            key1Active: true, key1LastUsed: "2024-06-01T00:00:00Z",
            key2Active: true, key2LastUsed: "2025-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Single(result);
    }

    [Fact]
    public void NoActiveKeys_IsExcluded()
    {
        var report = new[] { MakeKeyUser("alice") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("alice"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void StaleKey_UserNotInIamList_IsExcluded()
    {
        var report = new[] { MakeKeyUser("alice", key1Active: true, key1LastUsed: "2024-06-01T00:00:00Z") };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(report, IamNames("bob"), Cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void MixedReport_ReturnsOnlyUsersWithStaleActiveKeys()
    {
        var report = new[]
        {
            MakeKeyUser("stale-k1",   key1Active: true,  key1LastUsed: "2024-06-01T00:00:00Z"),  // included
            MakeKeyUser("stale-k2",   key2Active: true,  key2LastUsed: "2024-06-01T00:00:00Z"),  // included
            MakeKeyUser("recent",     key1Active: true,  key1LastUsed: "2025-06-01T00:00:00Z"),  // excluded – used recently
            MakeKeyUser("inactive",   key1Active: false, key1LastUsed: "2024-06-01T00:00:00Z"),  // excluded – key inactive
            MakeKeyUser("nokeys"),                                                                // excluded – no keys
            MakeKeyUser("notiniam",   key1Active: true,  key1LastUsed: "2024-06-01T00:00:00Z"),  // excluded – not in IAM list
        };

        var result = CredentialReportFilters.GetUsersWithStaleAccessKeys(
            report,
            IamNames("stale-k1", "stale-k2", "recent", "inactive", "nokeys"),
            Cutoff);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.User == "stale-k1");
        Assert.Contains(result, r => r.User == "stale-k2");
    }

    // -----------------------------------------------------------------------
    // TryParseAwsDate
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("no_information")]
    [InlineData("not_supported")]
    public void TryParseAwsDate_Sentinels_ReturnFalse(string? value)
    {
        var parsed = CredentialReportFilters.TryParseAwsDate(value, out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData("2024-06-15T10:30:00Z")]
    [InlineData("2024-06-15T10:30:00+00:00")]
    [InlineData("2023-01-01T00:00:00Z")]
    public void TryParseAwsDate_ValidIso8601_ReturnsTrueAndParsesUtc(string value)
    {
        var parsed = CredentialReportFilters.TryParseAwsDate(value, out var result);

        Assert.True(parsed);
        Assert.NotEqual(default, result);
    }
}

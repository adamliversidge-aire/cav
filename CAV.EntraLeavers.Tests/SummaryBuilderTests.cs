using CAV.EntraLeavers.Entra.Models;
using CAV.EntraLeavers.Iam;
using CAV.EntraLeavers.Matching;
using CAV.EntraLeavers.Notifications;
using Xunit;

namespace CAV.EntraLeavers.Tests;

public class SummaryBuilderTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static RunContext Run(bool dryRun) => new("123456789012", 90, Cutoff, dryRun);

    private static LeaverOutcome Outcome(
        string name,
        bool console = true,
        string[]? keys = null,
        string? error = null) =>
        new(
            new MatchedUser(name, new GraphUser
            {
                DisplayName = "Alice Smith",
                Mail = name,
                SignInActivity = new SignInActivity { LastSignInDateTime = Cutoff.AddDays(-30) }
            }, MatchSource.UserName),
            new DisableResult(name, console, keys ?? [], error));

    [Fact]
    public void NothingChanged_ReturnsNull()
    {
        var result = SummaryBuilder.Build(Run(dryRun: false), [Outcome("a@example.com", console: false)]);

        Assert.Null(result);
    }

    [Fact]
    public void DryRun_SubjectAndBodyAreMarked()
    {
        var result = SummaryBuilder.Build(Run(dryRun: true), [Outcome("a@example.com")]);

        Assert.NotNull(result);
        Assert.StartsWith("[DRY RUN] ", result.Subject);
        Assert.Contains("1 IAM user(s) to disable", result.Subject);
        Assert.Contains("WOULD be disabled", result.Message);
    }

    [Fact]
    public void LiveRun_ListsUserActionsAndSignIn()
    {
        var result = SummaryBuilder.Build(
            Run(dryRun: false),
            [Outcome("a@example.com", keys: ["AKIAEXAMPLE1234"])]);

        Assert.NotNull(result);
        Assert.DoesNotContain("DRY RUN", result.Subject);
        Assert.Contains("a@example.com (matched on user name)", result.Message);
        Assert.Contains("last sign-in 2026-06-01", result.Message);
        Assert.Contains("console password removed", result.Message);
        Assert.Contains("****1234", result.Message);
        Assert.DoesNotContain("AKIAEXAMPLE1234", result.Message);
        Assert.Contains("123456789012", result.Message);
    }

    [Fact]
    public void Failures_AreListedAndCountedInSubject()
    {
        var result = SummaryBuilder.Build(
            Run(dryRun: false),
            [Outcome("a@example.com"), Outcome("b@example.com", error: "AccessDenied")]);

        Assert.NotNull(result);
        Assert.EndsWith("1 IAM user(s) disabled, 1 failed", result.Subject);
        Assert.Contains("Error: AccessDenied", result.Message);
    }

    [Fact]
    public void LimitExceeded_SaysNoUsersWereDisabled()
    {
        var result = SummaryBuilder.BuildLimitExceeded(Run(dryRun: false), 40, 25);

        Assert.Contains("ABORTED", result.Subject);
        Assert.Contains("No users were disabled", result.Message);
    }

    [Fact]
    public void Subjects_FitSnsLimit()
    {
        var outcomes = Enumerable.Range(0, 999).Select(i => Outcome($"u{i}@example.com")).ToList();
        outcomes.AddRange(Enumerable.Range(0, 999).Select(i => Outcome($"f{i}@example.com", error: "x")));

        Assert.True(SummaryBuilder.Build(Run(dryRun: true), outcomes)!.Subject.Length <= 100);
        Assert.True(SummaryBuilder.BuildLimitExceeded(Run(dryRun: false), 99999, 25).Subject.Length <= 100);
    }
}

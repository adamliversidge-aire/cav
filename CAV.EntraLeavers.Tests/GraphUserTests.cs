using CAV.EntraLeavers.Entra.Models;
using Xunit;

namespace CAV.EntraLeavers.Tests;

public class GraphUserTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AllSignInsBeforeCutoff_IsInactive()
    {
        var user = new GraphUser
        {
            SignInActivity = new SignInActivity
            {
                LastSignInDateTime = Cutoff.AddDays(-10),
                LastNonInteractiveSignInDateTime = Cutoff.AddDays(-5)
            }
        };

        Assert.True(user.IsInactiveSince(Cutoff));
        Assert.Equal(Cutoff.AddDays(-5), user.LastActivity);
    }

    [Fact]
    public void RecentNonInteractiveSignIn_IsNotInactive()
    {
        var user = new GraphUser
        {
            SignInActivity = new SignInActivity
            {
                LastSignInDateTime = Cutoff.AddDays(-100),
                LastNonInteractiveSignInDateTime = Cutoff.AddDays(1)
            }
        };

        Assert.False(user.IsInactiveSince(Cutoff));
    }

    [Fact]
    public void NoSignInData_IsNotInactive()
    {
        Assert.False(new GraphUser().IsInactiveSince(Cutoff));
    }

    [Fact]
    public void MailAndUpnSameIgnoringCase_ReturnsOneAddress()
    {
        var user = new GraphUser { Mail = "Alice@Example.com", UserPrincipalName = "alice@example.com" };

        Assert.Single(user.EmailAddresses);
    }
}

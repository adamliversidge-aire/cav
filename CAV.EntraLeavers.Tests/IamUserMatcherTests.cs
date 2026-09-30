using CAV.EntraLeavers.Entra.Models;
using CAV.EntraLeavers.Matching;
using Xunit;

namespace CAV.EntraLeavers.Tests;

public class IamUserMatcherTests
{
    private static GraphUser MakeEntraUser(string? mail, string? upn = null) =>
        new() { Id = Guid.NewGuid().ToString(), DisplayName = mail, Mail = mail, UserPrincipalName = upn ?? mail };

    private static IReadOnlyDictionary<string, GraphUser> Index(params GraphUser[] users) =>
        IamUserMatcher.BuildEmailIndex(users);

    [Fact]
    public void UserName_EqualsMail_IsMatchedOnUserName()
    {
        var alice = MakeEntraUser("alice@example.com");

        var result = IamUserMatcher.Match(Index(alice), [new IamIdentity("alice@example.com")]);

        var match = Assert.Single(result);
        Assert.Equal("alice@example.com", match.IamUserName);
        Assert.Same(alice, match.EntraUser);
        Assert.Equal(MatchSource.UserName, match.Source);
    }

    [Fact]
    public void UserName_EqualsUpnButNotMail_IsMatched()
    {
        var alice = MakeEntraUser("alice.smith@example.com", upn: "asmith@example.onmicrosoft.com");

        var result = IamUserMatcher.Match(Index(alice), [new IamIdentity("asmith@example.onmicrosoft.com")]);

        Assert.Single(result);
    }

    [Fact]
    public void UserName_DifferentCase_IsMatched()
    {
        var result = IamUserMatcher.Match(
            Index(MakeEntraUser("Alice@Example.com")),
            [new IamIdentity("alice@example.COM")]);

        Assert.Single(result);
    }

    [Fact]
    public void EmailTag_EqualsMail_IsMatchedOnEmailTag()
    {
        var result = IamUserMatcher.Match(
            Index(MakeEntraUser("alice@example.com")),
            [new IamIdentity("alice", EmailTag: " ALICE@example.com ")]);

        var match = Assert.Single(result);
        Assert.Equal("alice", match.IamUserName);
        Assert.Equal(MatchSource.EmailTag, match.Source);
    }

    [Fact]
    public void NoNameOrTagMatch_IsExcluded()
    {
        var result = IamUserMatcher.Match(
            Index(MakeEntraUser("alice@example.com")),
            [new IamIdentity("bob"), new IamIdentity("carol", EmailTag: "carol@example.com")]);

        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateIamUser_IsMatchedOnce()
    {
        var result = IamUserMatcher.Match(
            Index(MakeEntraUser("alice@example.com")),
            [new IamIdentity("alice@example.com"), new IamIdentity("alice@example.com")]);

        Assert.Single(result);
    }

    [Fact]
    public void TwoIamUsers_ForSameEntraUser_AreBothMatched()
    {
        var result = IamUserMatcher.Match(
            Index(MakeEntraUser("alice@example.com")),
            [new IamIdentity("alice@example.com"), new IamIdentity("alice-admin", EmailTag: "alice@example.com")]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void EntraUser_WithNoEmail_IsNotIndexed()
    {
        var index = Index(MakeEntraUser(null, upn: null));

        Assert.Empty(index);
    }
}

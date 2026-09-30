using CAV.EntraLeavers.Entra.Models;

namespace CAV.EntraLeavers.Matching;

/// <summary>An IAM user and, if it has one, the value of its email tag.</summary>
public sealed record IamIdentity(string UserName, string? EmailTag = null);

public enum MatchSource
{
    UserName,
    EmailTag
}

public sealed record MatchedUser(string IamUserName, GraphUser EntraUser, MatchSource Source);

public static class IamUserMatcher
{
    /// <summary>Case-insensitive lookup of every mail/UPN address to its Entra user.</summary>
    public static IReadOnlyDictionary<string, GraphUser> BuildEmailIndex(IEnumerable<GraphUser> entraUsers)
    {
        var index = new Dictionary<string, GraphUser>(StringComparer.OrdinalIgnoreCase);

        foreach (var user in entraUsers)
        {
            foreach (var email in user.EmailAddresses)
            {
                index.TryAdd(email, user);
            }
        }

        return index;
    }

    /// <summary>
    /// Matches IAM users to stale Entra users when the IAM user name, or its email tag,
    /// equals the Entra mail or UPN. Each IAM user is matched at most once, user name first.
    /// </summary>
    public static IReadOnlyList<MatchedUser> Match(
        IReadOnlyDictionary<string, GraphUser> entraEmailIndex,
        IEnumerable<IamIdentity> iamUsers)
    {
        List<MatchedUser> matches = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var iamUser in iamUsers)
        {
            if (!seen.Add(iamUser.UserName))
            {
                continue;
            }

            if (entraEmailIndex.TryGetValue(iamUser.UserName, out var byName))
            {
                matches.Add(new MatchedUser(iamUser.UserName, byName, MatchSource.UserName));
            }
            else if (!string.IsNullOrWhiteSpace(iamUser.EmailTag)
                     && entraEmailIndex.TryGetValue(iamUser.EmailTag.Trim(), out var byTag))
            {
                matches.Add(new MatchedUser(iamUser.UserName, byTag, MatchSource.EmailTag));
            }
        }

        return matches;
    }
}

using System.Text.Json.Serialization;

namespace CAV.EntraLeavers.Entra.Models;

public sealed class GraphUser
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Mail { get; set; }
    public string? UserPrincipalName { get; set; }
    public bool? AccountEnabled { get; set; }
    public SignInActivity? SignInActivity { get; set; }

    /// <summary>Most recent sign-in of any kind (interactive, non-interactive or successful).</summary>
    [JsonIgnore]
    public DateTimeOffset? LastActivity =>
        new[]
        {
            SignInActivity?.LastSignInDateTime,
            SignInActivity?.LastNonInteractiveSignInDateTime,
            SignInActivity?.LastSuccessfulSignInDateTime
        }.Max();

    /// <summary>
    /// Graph filters on interactive sign-ins only, so re-check every sign-in type to avoid
    /// flagging someone who is still active through non-interactive (token refresh) sign-ins.
    /// </summary>
    public bool IsInactiveSince(DateTimeOffset cutoff) => LastActivity is { } last && last <= cutoff;

    /// <summary>Mail and UPN, de-duplicated, for matching against IAM.</summary>
    [JsonIgnore]
    public IEnumerable<string> EmailAddresses =>
        new[] { Mail, UserPrincipalName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class SignInActivity
{
    public DateTimeOffset? LastSignInDateTime { get; set; }
    public DateTimeOffset? LastNonInteractiveSignInDateTime { get; set; }
    public DateTimeOffset? LastSuccessfulSignInDateTime { get; set; }
}

internal sealed class GraphUserPage
{
    [JsonPropertyName("value")]
    public List<GraphUser> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

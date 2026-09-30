using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CAV.EntraLeavers.Entra.Models;

namespace CAV.EntraLeavers.Entra;

public interface IEntraGraphClient
{
    /// <summary>Returns Entra users whose last sign-in was on or before <paramref name="cutoff"/>.</summary>
    Task<IReadOnlyList<GraphUser>> GetUsersInactiveSinceAsync(
        EntraCredentials credentials,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal Microsoft Graph client using the OAuth2 client-credentials flow.
/// Requires the application permissions User.Read.All and AuditLog.Read.All (admin consented).
/// </summary>
public sealed class EntraGraphClient(HttpClient httpClient) : IEntraGraphClient
{
    internal const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    internal const string LoginBaseUrl = "https://login.microsoftonline.com";
    private const int MaxThrottleRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GraphUser>> GetUsersInactiveSinceAsync(
        EntraCredentials credentials,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(credentials, cancellationToken);

        List<GraphUser> users = [];
        string? url = BuildInactiveUsersUrl(cutoff);

        while (url is not null)
        {
            var page = await GetPageAsync(url, token, cancellationToken);
            users.AddRange(page.Value);
            url = page.NextLink;
        }

        return users;
    }

    internal static string BuildInactiveUsersUrl(DateTimeOffset cutoff)
    {
        var cutoffText = cutoff.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var filter = Uri.EscapeDataString($"signInActivity/lastSignInDateTime le {cutoffText}");
        const string select = "id,displayName,mail,userPrincipalName,accountEnabled,signInActivity";

        // 999 is the maximum page size Graph allows when signInActivity is selected.
        return $"{GraphBaseUrl}/users?$filter={filter}&$select={select}&$top=999";
    }

    private async Task<string> GetAccessTokenAsync(EntraCredentials credentials, CancellationToken cancellationToken)
    {
        var tokenUrl = $"{LoginBaseUrl}/{Uri.EscapeDataString(credentials.TenantId)}/oauth2/v2.0/token";

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["scope"] = "https://graph.microsoft.com/.default"
        });

        using var response = await httpClient.PostAsync(tokenUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The error body contains error/error_description only, never the secret.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Entra token request failed ({(int)response.StatusCode}): {body}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);

        return string.IsNullOrWhiteSpace(tokenResponse?.AccessToken)
            ? throw new InvalidOperationException("Entra token response did not contain an access_token.")
            : tokenResponse.AccessToken;
    }

    private async Task<GraphUserPage> GetPageAsync(string url, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxThrottleRetries)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Graph users query failed ({(int)response.StatusCode}): {body}");
            }

            return await response.Content.ReadFromJsonAsync<GraphUserPage>(JsonOptions, cancellationToken)
                   ?? new GraphUserPage();
        }
    }
}

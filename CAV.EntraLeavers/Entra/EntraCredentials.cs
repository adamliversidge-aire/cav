using System.Text.Json;

namespace CAV.EntraLeavers.Entra;

/// <summary>
/// Entra app registration details, stored as JSON in Secrets Manager:
/// {"tenantId":"...","clientId":"...","clientSecret":"..."}
/// </summary>
public sealed record EntraCredentials(string TenantId, string ClientId, string ClientSecret)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EntraCredentials FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Entra secret is empty.");
        }

        var credentials = JsonSerializer.Deserialize<EntraCredentials>(json, JsonOptions);

        if (credentials is null
            || string.IsNullOrWhiteSpace(credentials.TenantId)
            || string.IsNullOrWhiteSpace(credentials.ClientId)
            || string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            throw new InvalidOperationException("Entra secret must contain tenantId, clientId and clientSecret.");
        }

        return credentials;
    }

    // Keep the secret out of logs if the record is ever printed.
    public override string ToString() => $"EntraCredentials {{ TenantId = {TenantId}, ClientId = {ClientId} }}";
}

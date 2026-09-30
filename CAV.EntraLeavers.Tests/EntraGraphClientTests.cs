using System.Net;
using System.Text;
using CAV.EntraLeavers.Entra;
using Xunit;

namespace CAV.EntraLeavers.Tests;

public class EntraGraphClientTests
{
    private static readonly EntraCredentials Credentials = new("tenant-id", "client-id", "client-secret");
    private static readonly DateTimeOffset Cutoff = new(2026, 7, 2, 8, 30, 0, TimeSpan.Zero);

    /// <summary>Replays queued responses and records each request (with its body) for assertions.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        public FakeHandler Returns(HttpStatusCode status, string json) =>
            Returns(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        public FakeHandler Returns(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return _responses.Dequeue();
        }
    }

    private const string TokenJson = """{"token_type":"Bearer","access_token":"token-123","expires_in":3599}""";

    [Fact]
    public async Task TokenRequest_UsesClientCredentialsFlow()
    {
        var handler = new FakeHandler()
            .Returns(HttpStatusCode.OK, TokenJson)
            .Returns(HttpStatusCode.OK, """{"value":[]}""");

        await new EntraGraphClient(new HttpClient(handler)).GetUsersInactiveSinceAsync(Credentials, Cutoff);

        var (tokenRequest, body) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, tokenRequest.Method);
        Assert.Equal("https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token", tokenRequest.RequestUri!.ToString());
        Assert.Contains("grant_type=client_credentials", body);
        Assert.Contains("client_id=client-id", body);
        Assert.Contains("client_secret=client-secret", body);
        Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2F.default", body);
    }

    [Fact]
    public async Task UsersQuery_FiltersOnCutoffAndSendsBearerToken()
    {
        var handler = new FakeHandler()
            .Returns(HttpStatusCode.OK, TokenJson)
            .Returns(HttpStatusCode.OK, """{"value":[]}""");

        await new EntraGraphClient(new HttpClient(handler)).GetUsersInactiveSinceAsync(Credentials, Cutoff);

        var (usersRequest, _) = handler.Requests[1];
        var url = Uri.UnescapeDataString(usersRequest.RequestUri!.ToString());
        Assert.StartsWith("https://graph.microsoft.com/v1.0/users?", url);
        Assert.Contains("$filter=signInActivity/lastSignInDateTime le 2026-07-02T08:30:00Z", url);
        Assert.Contains("signInActivity", url[url.IndexOf("$select", StringComparison.Ordinal)..]);
        Assert.Equal("Bearer", usersRequest.Headers.Authorization!.Scheme);
        Assert.Equal("token-123", usersRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task NextLink_IsFollowedUntilLastPage()
    {
        var handler = new FakeHandler()
            .Returns(HttpStatusCode.OK, TokenJson)
            .Returns(HttpStatusCode.OK, """
                {"value":[{"id":"1","mail":"a@example.com","signInActivity":{"lastSignInDateTime":"2026-01-01T00:00:00Z"}}],
                 "@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=abc"}
                """)
            .Returns(HttpStatusCode.OK, """{"value":[{"id":"2","userPrincipalName":"b@example.com"}]}""");

        var users = await new EntraGraphClient(new HttpClient(handler)).GetUsersInactiveSinceAsync(Credentials, Cutoff);

        Assert.Equal(["1", "2"], users.Select(u => u.Id));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), users[0].SignInActivity!.LastSignInDateTime);
        Assert.Equal("https://graph.microsoft.com/v1.0/users?$skiptoken=abc", handler.Requests[2].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task TokenRequestRejected_ThrowsWithEntraError()
    {
        var handler = new FakeHandler()
            .Returns(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"AADSTS7000215"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EntraGraphClient(new HttpClient(handler)).GetUsersInactiveSinceAsync(Credentials, Cutoff));

        Assert.Contains("invalid_client", ex.Message);
    }

    [Fact]
    public async Task GraphForbidden_Throws()
    {
        var handler = new FakeHandler()
            .Returns(HttpStatusCode.OK, TokenJson)
            .Returns(HttpStatusCode.Forbidden, """{"error":{"code":"Authorization_RequestDenied"}}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EntraGraphClient(new HttpClient(handler)).GetUsersInactiveSinceAsync(Credentials, Cutoff));

        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task Throttled_RetriesAfterDelay()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);

        var handler = new FakeHandler()
            .Returns(HttpStatusCode.OK, TokenJson)
            .Returns(throttled)
            .Returns(HttpStatusCode.OK, """{"value":[{"id":"1"}]}""");

        var users = await new EntraGraphClient(new HttpClient(handler)).GetUsersInactiveSinceAsync(Credentials, Cutoff);

        Assert.Single(users);
        Assert.Equal(3, handler.Requests.Count);
    }
}

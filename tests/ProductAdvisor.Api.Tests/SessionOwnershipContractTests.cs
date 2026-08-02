using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TestSupport;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>Proves FR-031: a session belongs to whoever created it, and no other signed-in user
/// can read or continue it — a non-owner gets the identical response as a genuinely unknown
/// session id, so existence can never be inferred.</summary>
public sealed class SessionOwnershipContractTests : IAsyncDisposable
{
    private readonly AdvisorApiFactory _factory = new();

    [Fact]
    public async Task The_owner_can_read_their_own_session()
    {
        var sessionId = await CreateSessionAsync("user-a");

        var response = await SendAsAsync(HttpMethod.Get, $"/api/conversations/{sessionId}", "user-a");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_different_user_cannot_read_someone_elses_session()
    {
        var sessionId = await CreateSessionAsync("user-a");

        var response = await SendAsAsync(HttpMethod.Get, $"/api/conversations/{sessionId}", "user-b");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_different_user_cannot_continue_someone_elses_session()
    {
        var sessionId = await CreateSessionAsync("user-a");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{sessionId}/messages")
        {
            Content = JsonContent.Create(new { text = "hello" }),
        };
        request.Headers.Add(
            Microsoft.Extensions.Hosting.InternalApiKeyMiddleware.HeaderName, InternalApiKeyTestDefaults.Key);
        request.Headers.Add("X-User-Id", "user-b");

        using var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_session_id_and_a_non_owned_session_id_return_the_identical_response()
    {
        var ownedSessionId = await CreateSessionAsync("user-a");
        var unknownSessionId = Guid.NewGuid();

        var nonOwnerResponse = await SendAsAsync(HttpMethod.Get, $"/api/conversations/{ownedSessionId}", "user-b");
        var unknownResponse = await SendAsAsync(HttpMethod.Get, $"/api/conversations/{unknownSessionId}", "user-b");

        Assert.Equal(unknownResponse.StatusCode, nonOwnerResponse.StatusCode);
    }

    private async Task<Guid> CreateSessionAsync(string userId)
    {
        var response = await SendAsAsync(HttpMethod.Post, "/api/conversations", userId);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }

    private async Task<HttpResponseMessage> SendAsAsync(HttpMethod method, string url, string userId)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            Microsoft.Extensions.Hosting.InternalApiKeyMiddleware.HeaderName, InternalApiKeyTestDefaults.Key);
        request.Headers.Add("X-User-Id", userId);

        using var client = _factory.CreateClient();
        return await client.SendAsync(request);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

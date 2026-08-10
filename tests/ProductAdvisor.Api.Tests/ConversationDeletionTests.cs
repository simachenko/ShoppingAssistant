using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Contracts;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 12 (spec.md FR-119): a signed-in user can delete their own conversation history — a
/// deleted session is no longer retrievable through this system's own APIs, and a deletion
/// request while a turn is still in flight for that session is rejected (409), mirroring FR-024's
/// own conflict response rather than deleting out from under an in-progress turn. NOT run in this
/// sandbox — like the rest of this test project, it requires a Testcontainers Postgres instance
/// (Docker), unavailable here.
/// </summary>
public sealed class ConversationDeletionTests : IAsyncDisposable
{
    private readonly AdvisorApiFactory _factory = new();

    [Fact]
    public async Task A_deleted_session_returns_404_on_a_subsequent_GET()
    {
        var client = _factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/conversations/{sessionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/conversations/{sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task A_deleted_session_returns_404_on_a_subsequent_message()
    {
        var client = _factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        await client.DeleteAsync($"/api/conversations/{sessionId}");

        var postResponse = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("hello again"));
        Assert.Equal(HttpStatusCode.NotFound, postResponse.StatusCode);
    }

    [Fact]
    public async Task Deleting_all_of_a_users_sessions_removes_every_one_of_them()
    {
        var client = _factory.CreateAuthenticatedClient();
        var sessionA = await CreateSessionAsync(client);
        var sessionB = await CreateSessionAsync(client);

        var deleteResponse = await client.DeleteAsync("/api/conversations");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{sessionA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{sessionB}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_session_while_a_turn_is_in_flight_for_it_returns_409()
    {
        var blockingChatClient = new BlockingChatClient();
        _factory.ChatClientOverride = blockingChatClient;

        var client = _factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var messageTask = client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("I need a good laptop"));
        await blockingChatClient.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var deleteResponse = await client.DeleteAsync($"/api/conversations/{sessionId}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        blockingChatClient.Release();
        await messageTask;
    }

    [Fact]
    public async Task Deleting_an_unknown_session_returns_the_same_404_as_any_other_unknown_or_not_owned_id()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.DeleteAsync($"/api/conversations/{Guid.NewGuid()}");

        // This endpoint never reveals whether an id ever existed — mirroring the identical-404
        // posture every other session-scoped endpoint already takes for a non-owned/unknown id
        // (FR-031) — rather than distinguishing "never existed" from "already deleted".
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_same_session_twice_is_safe_the_second_call_returns_404_not_an_error()
    {
        var client = _factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var first = await client.DeleteAsync($"/api/conversations/{sessionId}");
        var second = await client.DeleteAsync($"/api/conversations/{sessionId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode); // no longer owned/found — never a crash.
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private sealed class BlockingChatClient : IChatClient
    {
        public TaskCompletionSource CallStarted { get; } = new();
        private readonly TaskCompletionSource _release = new();

        public void Release() => _release.TrySetResult();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "What's your budget for this laptop?"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by this test.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

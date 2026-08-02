using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>Proves FR-024/SC-014: a second message for a session is rejected while the first is
/// still being processed, never processed concurrently with it.</summary>
public sealed class ConcurrentMessageRejectionTests : IAsyncDisposable
{
    private readonly AdvisorApiFactory _factory = new();

    [Fact]
    public async Task A_second_message_while_the_first_is_still_processing_returns_409()
    {
        var blockingChatClient = new BlockingChatClient();
        _factory.ChatClientOverride = blockingChatClient;

        var client = _factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var firstCallTask = client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new { text = "I need a good laptop" });

        // Wait until the first request has actually entered the handler and is blocked inside
        // the chat client call, so the gate is genuinely held before we send the second request.
        await blockingChatClient.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new { text = "a second, overlapping message" });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        blockingChatClient.Release();
        var firstResponse = await firstCallTask;
        Assert.True(firstResponse.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_message_sent_after_the_prior_turn_finished_succeeds_normally()
    {
        var readyChatClient = new BlockingChatClient();
        readyChatClient.Release();
        _factory.ChatClientOverride = readyChatClient;

        var client = _factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new { text = "I need a good laptop" });
        Assert.True(firstResponse.IsSuccessStatusCode);

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new { text = "up to 30000 UAH" });
        Assert.True(secondResponse.IsSuccessStatusCode);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>An IChatClient that signals <see cref="CallStarted"/> the instant it's invoked,
    /// then blocks until <see cref="Release"/> is called — gives a test a reliable window in
    /// which a turn is provably still in flight.</summary>
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

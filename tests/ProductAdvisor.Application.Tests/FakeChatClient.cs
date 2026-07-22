using Microsoft.Extensions.AI;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// A stand-in for the real (function-invoking, LLM-backed) chat client. It returns a canned
/// final response, as if any tool calls the real middleware would have made had already
/// resolved — letting these tests focus purely on what <see cref="ConversationOrchestrator"/>
/// itself does with that response, without needing a live LLM or Docker.
/// </summary>
public sealed class FakeChatClient(string responseText) : IChatClient
{
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by these tests.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

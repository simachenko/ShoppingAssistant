using Microsoft.Extensions.AI;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// A two-call fake (extraction, then the route-specific call) whose SECOND call behavior is
/// fully scripted — used only by <see cref="TurnResourceBudgetTests"/> to simulate conditions
/// <see cref="FakeChatClient"/> can't express: a hang, a thrown exception (simulating the shared
/// chat client's own consecutive-tool-error budget being exhausted), or a response ending
/// mid-tool-call (simulating its iteration budget being exhausted).
/// </summary>
public sealed class ScriptedChatClient(string extractionJson, Func<CancellationToken, Task<ChatResponse>> secondCall) : IChatClient
{
    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return CallCount == 1
            ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, extractionJson)))
            : secondCall(cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by these tests.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

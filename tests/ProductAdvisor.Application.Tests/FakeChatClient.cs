using Microsoft.Extensions.AI;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// A stand-in for the real (function-invoking, LLM-backed) chat client. Returns queued canned
/// responses in call order — the turn-processing cycle always calls the extraction stage first
/// (spec.md FR-038), then (for most routes) exactly one narration/tool-invocation call, so a
/// test supplies its responses in that same order: <c>new FakeChatClient(extractionJson,
/// narrationText)</c>. If the queue is exhausted, the last supplied response repeats — handy for
/// tests that only care about one of the two calls.
/// </summary>
public sealed class FakeChatClient(params string[] responses) : IChatClient
{
    private readonly Queue<string> _responses = new(responses);
    private string _lastResponse = responses.Length > 0 ? responses[^1] : string.Empty;
    private readonly List<IReadOnlyList<ChatMessage>> _callHistory = [];

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }
    public int CallCount { get; private set; }
    public IReadOnlyList<IReadOnlyList<ChatMessage>> CallHistory => _callHistory;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        LastMessages = messageList;
        LastOptions = options;
        CallCount++;
        _callHistory.Add(messageList);

        var text = _responses.Count > 0 ? _responses.Dequeue() : _lastResponse;
        if (_responses.Count == 0)
        {
            _lastResponse = text;
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by these tests.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

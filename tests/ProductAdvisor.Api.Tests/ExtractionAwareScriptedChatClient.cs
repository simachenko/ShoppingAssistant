using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Extraction-aware sibling of <see cref="ScriptedChatClient"/> for Phase 10's tool-recipe
/// scoping tests: the turn-processing cycle's FIRST call is always structured-intent extraction
/// (spec.md FR-038/FR-048) — a route-specific call is never reachable without a schema-valid
/// result from it first. Records the tool names offered on every call so a test can assert
/// exactly which tools a route's recipe exposed (FR-066–FR-070), without needing to actually
/// invoke any of them.
/// </summary>
public sealed class ExtractionAwareScriptedChatClient(string extractionJson, string narrationText) : IChatClient
{
    private int _callCount;

    public List<IReadOnlyList<string>> OfferedToolNamesPerCall { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _callCount++;
        OfferedToolNamesPerCall.Add(options?.Tools?.Select(t => t.Name).ToList() ?? []);

        var text = _callCount == 1 ? extractionJson : narrationText;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

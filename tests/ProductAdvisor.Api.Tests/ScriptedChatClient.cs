using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Stands in for a real LLM: when scripted with a tool name, it actually invokes that
/// <see cref="AIFunction"/> from the options passed to it (the same tool instances the real app
/// registered), so the conversation-API-level tests exercise the genuine tool handler and
/// <see cref="ProductAdvisor.Application.IToolResultCapture"/> path end-to-end without a live LLM.
/// </summary>
/// <remarks>
/// The turn-processing cycle's FIRST call is always structured-intent extraction (spec.md
/// FR-038/FR-048), and no route is reachable without a schema-valid result from it. So
/// <paramref name="extractionJson"/> is answered first and the scripted tool/narration only from
/// the second call onward. Before this, every call returned <paramref name="narrationText"/> —
/// extraction received prose instead of JSON, failed schema validation twice, and every one of
/// these tests silently became a `clarification` turn.
/// </remarks>
public sealed class ScriptedChatClient(
    string? toolNameToCall,
    IDictionary<string, object?>? toolArguments,
    string narrationText,
    bool throwMidStream = false,
    string extractionJson = ScriptedChatClient.RecommendSmartphonesExtraction) : IChatClient
{
    /// <summary>
    /// A schema-valid `recommend` extraction with the essential fields present, which is what
    /// most conversation-API tests need to reach their route. Tests asserting a different intent
    /// pass their own.
    /// </summary>
    public const string RecommendSmartphonesExtraction =
        """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":15000,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""";

    private int _callCount;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (++_callCount == 1)
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, extractionJson));
        }

        await InvokeScriptedToolAsync(options, cancellationToken);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, narrationText));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (++_callCount == 1)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, extractionJson);
            yield break;
        }

        await InvokeScriptedToolAsync(options, cancellationToken);

        var words = narrationText.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (throwMidStream && i == words.Length / 2)
            {
                // Simulates a provider connection dropping mid-stream (constitution Principle V /
                // FR-015 fallback requirement) — the caller must never see a `result` event after this.
                throw new InvalidOperationException("Simulated provider stream interruption.");
            }

            var chunk = i == words.Length - 1 ? words[i] : words[i] + " ";
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }
    }

    private async Task InvokeScriptedToolAsync(ChatOptions? options, CancellationToken cancellationToken)
    {
        if (toolNameToCall is null)
        {
            return;
        }

        var tool = options?.Tools?.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolNameToCall)
            ?? throw new InvalidOperationException($"Tool '{toolNameToCall}' was not offered to the chat client this turn.");

        await tool.InvokeAsync(new AIFunctionArguments(toolArguments), cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

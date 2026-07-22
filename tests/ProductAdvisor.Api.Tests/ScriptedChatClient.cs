using Microsoft.Extensions.AI;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Stands in for a real LLM: when scripted with a tool name, it actually invokes that
/// <see cref="AIFunction"/> from the options passed to it (the same tool instances the real app
/// registered), so the conversation-API-level tests exercise the genuine tool handler and
/// <see cref="ProductAdvisor.Application.IToolResultCapture"/> path end-to-end without a live LLM.
/// </summary>
public sealed class ScriptedChatClient(string? toolNameToCall, IDictionary<string, object?>? toolArguments, string narrationText) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (toolNameToCall is not null)
        {
            var tool = options?.Tools?.OfType<AIFunction>().FirstOrDefault(t => t.Name == toolNameToCall)
                ?? throw new InvalidOperationException($"Tool '{toolNameToCall}' was not offered to the chat client this turn.");

            await tool.InvokeAsync(new AIFunctionArguments(toolArguments), cancellationToken);
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, narrationText));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by these tests.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

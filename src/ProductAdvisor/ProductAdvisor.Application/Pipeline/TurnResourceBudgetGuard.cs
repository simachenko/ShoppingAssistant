using Microsoft.Extensions.AI;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Enforces spec.md FR-071–FR-079 (data-model.md `TurnResourceBudget`) around one turn's
/// processing. The two loop-shaped limits — max tool calls per turn, max consecutive tool
/// errors — are enforced by the shared <see cref="IChatClient"/>'s own
/// <c>FunctionInvokingChatClient</c> configuration (<c>AdvisorAiExtensions</c>,
/// <c>MaximumIterationsPerRequest</c>/<c>MaximumConsecutiveErrorsPerRequest</c>) rather than
/// re-implemented here — that is the framework's own supported mechanism for the same guarantee.
/// Confirmed empirically (a throwaway test harness, not documentation, since the library's
/// behavior here isn't obvious from its public surface alone): reaching the iteration limit
/// returns the in-progress response gracefully, ending mid-tool-call, rather than throwing;
/// reaching the consecutive-error limit re-throws the underlying tool exception out of
/// <c>GetResponseAsync</c>. <see cref="ExceededToolCallBudget"/> and the catch clauses in
/// <c>ConversationOrchestrator</c>'s legacy-tool bridge translate both outcomes into a typed
/// <c>error</c> <c>AdvisorTurnResult</c> instead of a raw exception or a silently-wrong result.
/// This guard itself adds the one limit that mechanism doesn't cover: the turn's overall
/// wall-clock timeout.
/// </summary>
public sealed class TurnResourceBudgetGuard(TurnResourceBudgetOptions options)
{
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.OverallTurnTimeout);

        try
        {
            return await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired, not the caller's token (a client disconnect) — that case is
            // left to propagate unhandled so no result is constructed or persisted, and the
            // FR-024 in-flight marker is released by the caller's own disconnect handling.
            throw new TurnBudgetExceededException("The request took too long to process.", degraded: true);
        }
    }

    /// <summary>
    /// True when the shared chat client's <c>MaximumIterationsPerRequest</c> was reached before a
    /// final answer was produced: the response ends with a tool call that was never invoked.
    /// </summary>
    public static bool ExceededToolCallBudget(ChatResponse response) =>
        response.Messages.Count > 0 && response.Messages[^1].Contents.Any(c => c is FunctionCallContent);
}

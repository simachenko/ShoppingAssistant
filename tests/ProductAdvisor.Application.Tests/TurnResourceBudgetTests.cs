using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 10 (spec.md FR-071–FR-079, data-model.md `TurnResourceBudget`): every hard limit has a
/// fail-safe, and reaching one always ends the turn in a typed `error` result — never a partial
/// success, an infinite loop, or an unhandled exception reaching the client — except a genuine
/// client disconnect, which is left to propagate unhandled by design (no result to persist).
/// </summary>
public class TurnResourceBudgetTests
{
    [Fact]
    public async Task RunAsync_converts_its_own_timeout_into_a_degraded_budget_exceeded_error()
    {
        var guard = new TurnResourceBudgetGuard(new TurnResourceBudgetOptions { OverallTurnTimeout = TimeSpan.FromMilliseconds(20) });

        var ex = await Assert.ThrowsAsync<TurnBudgetExceededException>(() =>
            guard.RunAsync(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return "unreachable";
            }, CancellationToken.None));

        Assert.True(ex.Degraded);
    }

    [Fact]
    public async Task RunAsync_lets_the_callers_own_cancellation_propagate_unhandled_instead_of_becoming_an_error_result()
    {
        // A genuine client disconnect (FR-024) — never translated into a persisted result.
        var guard = new TurnResourceBudgetGuard(new TurnResourceBudgetOptions { OverallTurnTimeout = TimeSpan.FromSeconds(30) });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            guard.RunAsync<string>(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult("unreachable");
                },
                cts.Token));
    }

    [Fact]
    public async Task Exceeding_the_overall_turn_timeout_ends_the_turn_as_a_degraded_error()
    {
        var chatClient = new ScriptedChatClient(
            ExtractionJson.ProductFact(["Nokia 3310 Pro"]),
            async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreachable"));
            });
        var orchestrator = BuildOrchestrator(chatClient, new TurnResourceBudgetOptions
        {
            OverallTurnTimeout = TimeSpan.FromMilliseconds(20),
        });

        var result = await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "Is the Nokia 3310 Pro in stock?", CancellationToken.None);

        Assert.Equal("error", result.Type);
        Assert.True(result.Degraded);
    }

    [Fact]
    public async Task A_tool_call_exceeding_the_shared_clients_consecutive_error_budget_ends_the_turn_as_a_degraded_error()
    {
        // Simulates the shared FunctionInvokingChatClient's own MaximumConsecutiveErrorsPerRequest
        // being exhausted, which re-throws the underlying tool exception out of GetResponseAsync
        // (see TurnResourceBudgetGuard's doc comment for how this was verified).
        var chatClient = new ScriptedChatClient(
            ExtractionJson.ProductFact(["Nokia 3310 Pro"]),
            _ => throw new InvalidOperationException("the check_price_and_availability tool failed"));
        var orchestrator = BuildOrchestrator(chatClient, new TurnResourceBudgetOptions());

        var result = await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "Is the Nokia 3310 Pro in stock?", CancellationToken.None);

        Assert.Equal("error", result.Type);
        Assert.True(result.Degraded);
    }

    [Fact]
    public async Task A_turn_that_exhausts_the_shared_clients_iteration_budget_ends_as_a_degraded_error_with_zero_further_tool_calls()
    {
        // Simulates the shared FunctionInvokingChatClient's own MaximumIterationsPerRequest being
        // reached: it returns gracefully with a trailing, never-invoked tool call rather than
        // throwing (verified the same way).
        var chatClient = new ScriptedChatClient(
            ExtractionJson.ProductFact(["Nokia 3310 Pro"]),
            _ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call1", "check_price_and_availability", new Dictionary<string, object?>()),
            ]))));
        var orchestrator = BuildOrchestrator(chatClient, new TurnResourceBudgetOptions());

        var result = await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "Is the Nokia 3310 Pro in stock?", CancellationToken.None);

        Assert.Equal("error", result.Type);
        Assert.True(result.Degraded);
        Assert.Equal(2, chatClient.CallCount); // extraction + exactly the one scripted call — no retry loop.
    }

    private static ConversationOrchestrator BuildOrchestrator(IChatClient chatClient, TurnResourceBudgetOptions options) =>
        new(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            new TurnResourceBudgetGuard(options));
}

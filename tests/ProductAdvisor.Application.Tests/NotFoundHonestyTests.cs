using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Proves the US3 "not found" acceptance scenario at the orchestration layer: when
/// <c>get_product_details</c> resolves to <c>{ found: false }</c> — a data-access tool result
/// that, unlike a compute tool's output, is never routed through <see cref="IToolResultCapture"/>
/// — the LLM's own "not found" narration is relayed as an honest `answer`, never a fabricated
/// <see cref="Recommendation"/> or <see cref="Comparison"/> (research.md §1). Updated for the
/// deterministic turn-processing cycle (spec.md FR-062/FR-063, research.md §20): a
/// `product_fact`-routed turn with nothing captured is `answer`, never a default `clarification`
/// — the earlier pre-cycle behavior (defaulting to "clarification" whenever nothing was
/// captured) was exactly the ambiguity FR-062 now forbids.
/// </summary>
public class NotFoundHonestyTests
{
    [Fact]
    public async Task A_not_found_narration_with_no_captured_tool_result_is_an_honest_answer_not_a_fabricated_result()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var capture = new ToolResultCapture();

        var chatClient = new FakeChatClient(
            ExtractionJson.ProductFact(["Nokia 3310 Pro"]),
            "I couldn't find that product in our catalog.");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), capture,
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(
            session, "Is the Nokia 3310 Pro in stock?", CancellationToken.None);

        Assert.Equal("answer", result.Type);
        Assert.Equal("I couldn't find that product in our catalog.", result.Message);
        Assert.Null(result.Recommendation);
        Assert.Null(result.Comparison);
    }

    [Fact]
    public async Task A_not_found_narration_does_not_advance_the_session_into_recommending_or_comparing_state()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var chatClient = new FakeChatClient(
            ExtractionJson.ProductFact(["XYZ9000"]),
            "That product doesn't exist in our catalog.");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

        await orchestrator.ProcessMessageAsync(session, "Tell me about the XYZ9000", CancellationToken.None);

        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.Empty(session.LastSearchResults);
    }

    [Fact]
    public async Task A_compare_route_that_cannot_resolve_products_this_turn_still_asks_honestly()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var chatClient = new FakeChatClient(
            ExtractionJson.Compare(["the first one", "the second one"]),
            "Which two products would you like to compare?");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(session, "compare the first one and the second one", CancellationToken.None);

        Assert.Equal("clarification", result.Type);
        Assert.Equal("Which two products would you like to compare?", result.Question);
    }
}

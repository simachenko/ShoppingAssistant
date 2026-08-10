using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Asserts the turn-processing cycle's stage order (spec.md FR-036): extraction always runs
/// first and is never repeated beyond one repair attempt; the route-specific step (narration or
/// the legacy tool-invocation bridge) always runs after routing, never before, and never more
/// than once per turn. Complements <see cref="ExtractionStageTests"/> (extraction's own
/// repair/fallback behavior) and <see cref="PolicyRouterTests"/> (routing determinism).
/// </summary>
public class TurnProcessingCycleTests
{
    [Fact]
    public async Task A_recommend_turn_calls_the_chat_client_exactly_twice_extraction_then_narration()
    {
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [],
            UnmetConstraintExplanation = "No smartphones under 15000 UAH.",
        };
        var chatClient = new FakeChatClient(
            ExtractionJson.Recommend("smartphones", 15000m, "UAH"),
            "Nothing quite fits, unfortunately.");
        var recommendationService = new FakeRecommendationService(recommendation);
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient), recommendationService, TestBudgetGuard.Generous);

        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var result = await orchestrator.ProcessMessageAsync(
            session, "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.Equal(2, chatClient.CallCount);
        Assert.Equal("recommendation", result.Type);
        Assert.Equal(1, recommendationService.CallCount);
    }

    [Fact]
    public async Task A_clarify_route_never_reaches_a_second_chat_client_call()
    {
        // Confidence below threshold — extraction succeeds, but routing sends this to
        // clarification without any narration/tool-invocation call (FR-036: no stage skipped or
        // duplicated, and clarification never needs the second call at all).
        var chatClient = new FakeChatClient(ExtractionJson.LowConfidence());
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous);

        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var result = await orchestrator.ProcessMessageAsync(session, "something vague", CancellationToken.None);

        Assert.Equal("clarification", result.Type);
        Assert.Equal(1, chatClient.CallCount); // extraction only — no narration call was needed
    }

    [Fact]
    public async Task Two_turns_with_identical_extraction_results_produce_the_identical_route_outcome()
    {
        var recommendation = new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] };

        var chatClient1 = new FakeChatClient(ExtractionJson.Recommend("smartphones", 15000m, "UAH"), "narration");
        var orchestrator1 = new ConversationOrchestrator(
            chatClient1, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient1),
            new FakeRecommendationService(recommendation), TestBudgetGuard.Generous);

        var chatClient2 = new FakeChatClient(ExtractionJson.Recommend("smartphones", 15000m, "UAH"), "narration");
        var orchestrator2 = new ConversationOrchestrator(
            chatClient2, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient2),
            new FakeRecommendationService(recommendation), TestBudgetGuard.Generous);

        var result1 = await orchestrator1.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "user-a"), "I need a smartphone under 15000 UAH", CancellationToken.None);
        var result2 = await orchestrator2.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "user-b"), "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.Equal(result1.Type, result2.Type);
    }
}

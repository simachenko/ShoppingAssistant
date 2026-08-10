using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Proves the conversation orchestration loop only ever relays what a tool/service already
/// computed — it never invokes ScoringPolicy, never builds a Recommendation itself, and never
/// invents a fact (research.md §1, plan.md Summary). Updated for the deterministic
/// turn-processing cycle (spec.md FR-036–FR-059, research.md §20): the `recommend` route now
/// calls <see cref="IRecommendationService"/> directly with the deterministically-merged
/// requirement, rather than trusting whatever a free-form tool call happened to capture.
/// </summary>
public class OrchestrationNeverComputesTests
{
    private static ProductCandidate Candidate(string name) => new()
    {
        ProductId = Guid.NewGuid(),
        Name = name,
        Price = new Money(14000m, "UAH"),
        PriceVerified = true,
    };

    [Fact]
    public async Task A_recommend_route_relays_the_service_result_verbatim_never_recomputing_it()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var expectedRecommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items =
            [
                new RecommendedItem
                {
                    Candidate = Candidate("Galaxy S24"),
                    MatchedRequirements = ["budget <= 15000 UAH"],
                    TradeOffs = ["no notable trade-off"],
                    Score = 2m,
                },
            ],
        };

        var chatClient = new FakeChatClient(
            ExtractionJson.Recommend("smartphones", 15000m, "UAH"),
            "Here's a smartphone that fits your budget.");
        var recommendationService = new FakeRecommendationService(expectedRecommendation);
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient), recommendationService, TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(session, "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.Equal("recommendation", result.Type);
        // Same instance, not a recomputed one — the orchestrator never called ScoringPolicy.
        Assert.Same(expectedRecommendation, result.Recommendation);
        Assert.Equal(ConversationState.Recommending, session.State);
        Assert.Equal("smartphones", session.CurrentRequirement.Category);
        Assert.Equal(new Money(15000m, "UAH"), session.CurrentRequirement.Budget);
        // The deterministic compute step read the already-merged requirement, never an argument
        // the language model reconstructed itself (FR-066).
        Assert.Same(session.CurrentRequirement, recommendationService.LastRequirement);
    }

    [Fact]
    public async Task An_extraction_result_the_router_sends_to_Clarify_never_reaches_the_recommendation_service()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var chatClient = new FakeChatClient(ExtractionJson.RequirementPatchOnly(category: "laptops", missingFields: ["Budget"]));
        var recommendationService = new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] });
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient), recommendationService, TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(session, "I need a good laptop", CancellationToken.None);

        Assert.Equal("clarification", result.Type);
        Assert.Null(result.Recommendation);
        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.NotNull(session.PendingClarification);
        Assert.Equal(0, recommendationService.CallCount);
        // The partial patch (category) still merged even though the turn overall clarifies.
        Assert.Equal("laptops", session.CurrentRequirement.Category);
    }

    [Fact]
    public async Task A_smalltalk_turn_never_calls_the_recommendation_service_and_carries_no_product_data()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var chatClient = new FakeChatClient(ExtractionJson.Smalltalk(), "Hi there! How can I help you shop today?");
        var recommendationService = new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] });
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient), recommendationService, TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(session, "hello", CancellationToken.None);

        Assert.Equal("answer", result.Type);
        Assert.Null(result.Recommendation);
        Assert.Equal(0, recommendationService.CallCount);
    }
}

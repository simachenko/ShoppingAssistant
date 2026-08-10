using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 10 (spec.md FR-060–FR-065, data-model.md `TurnResult`): every turn resolves to exactly
/// one of seven discriminated types, assigned by policy routing plus that route's validated tool
/// outcome — never inferred from narration text, and the absence of a
/// recommendation/comparison/checkoutLink never defaults a turn to `clarification`.
/// </summary>
public class TurnResultTypeTests
{
    [Fact]
    public async Task An_unsupported_intent_produces_the_unsupported_type_with_zero_additional_llm_calls()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Unsupported());
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "write me a poem", CancellationToken.None);

        Assert.Equal("unsupported", result.Type);
        Assert.Equal(1, chatClient.CallCount); // extraction only — HandleUnsupported makes no LLM call.
    }

    [Fact]
    public async Task A_smalltalk_intent_produces_the_answer_type_never_clarification()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Smalltalk(), "Hi there!");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "hello!", CancellationToken.None);

        Assert.Equal("answer", result.Type);
    }

    [Fact]
    public async Task A_recommend_route_with_no_qualifying_candidates_still_produces_recommendation_type_not_clarification()
    {
        // FR-062: the absence of a full match is a valid, typed `recommendation` outcome
        // (empty Items + UnmetConstraintExplanation), never remapped to `clarification`.
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [],
            UnmetConstraintExplanation = "No smartphones under 15000 UAH.",
        };
        var chatClient = new FakeChatClient(
            ExtractionJson.Recommend("smartphones", 15000m, "UAH"), "Nothing fits right now.");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance), new FakeRecommendationService(recommendation), TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

        var result = await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"), "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.Equal("recommendation", result.Type);
        Assert.Empty(result.Recommendation!.Items);
    }

    [Fact]
    public void ForError_produces_the_error_type_carrying_the_degraded_flag()
    {
        var retryable = AdvisorTurnResult.ForError("temporary failure", degraded: true);
        var permanent = AdvisorTurnResult.ForError("cannot be fulfilled", degraded: false);

        Assert.Equal("error", retryable.Type);
        Assert.True(retryable.Degraded);
        Assert.Equal("error", permanent.Type);
        Assert.False(permanent.Degraded);
    }

    [Fact]
    public void ConversationApiMapper_maps_an_error_result_to_the_error_response_shape()
    {
        var result = AdvisorTurnResult.ForError("temporary failure", degraded: true);

        var response = ConversationApiMapper.ToResponse(result);

        Assert.Equal("error", response.Type);
        Assert.Equal("temporary failure", response.Message);
        Assert.True(response.Degraded);
    }

    [Fact]
    public void ConversationApiMapper_carries_nearest_alternatives_onto_the_recommendation_response()
    {
        var candidate = new ProductCandidate
        {
            ProductId = Guid.NewGuid(),
            Name = "Expensive Phone",
            Price = new Money(16000m, "UAH"),
            PriceVerified = true,
        };
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [],
            UnmetConstraintExplanation = "No smartphones under 15000 UAH.",
            NearestAlternatives =
            [
                new NearestAlternative { Candidate = candidate, ViolatedConstraints = ["budget: exceeds ceiling"] },
            ],
        };
        var result = AdvisorTurnResult.ForRecommendation("Nothing fits.", recommendation);

        var response = ConversationApiMapper.ToResponse(result);

        var alternative = Assert.Single(response.NearestAlternatives!);
        Assert.Equal(candidate.ProductId, alternative.ProductId);
        Assert.Contains("budget: exceeds ceiling", alternative.ViolatedConstraints);
    }
}

using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Proves the conversation orchestration loop only ever relays what a tool already computed
/// (captured via <see cref="IToolResultCapture"/>) or what the LLM said in plain text — it never
/// invokes ScoringPolicy, never builds a Recommendation itself, and never invents a fact
/// (research.md §1, plan.md Summary).
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
    public async Task When_a_tool_already_captured_a_recommendation_the_orchestrator_relays_it_verbatim()
    {
        var session = new ConversationSession(Guid.NewGuid());
        var requirementUsed = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
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

        var capture = new ToolResultCapture();
        capture.SetRecommendation(expectedRecommendation, requirementUsed);

        var orchestrator = new ConversationOrchestrator(
            new FakeChatClient("Here's a smartphone that fits your budget."), new FakeToolCatalog(), capture);

        var result = await orchestrator.ProcessMessageAsync(session, "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.Equal("recommendation", result.Type);
        // Same instance, not a recomputed one — the orchestrator never called ScoringPolicy.
        Assert.Same(expectedRecommendation, result.Recommendation);
        Assert.Equal(ConversationState.Recommending, session.State);
        Assert.Equal(requirementUsed, session.CurrentRequirement);
    }

    [Fact]
    public async Task When_no_tool_captured_anything_the_orchestrator_treats_the_LLM_text_as_a_clarification()
    {
        var session = new ConversationSession(Guid.NewGuid());
        var capture = new ToolResultCapture();

        var orchestrator = new ConversationOrchestrator(
            new FakeChatClient("What's your budget for this laptop?"), new FakeToolCatalog(), capture);

        var result = await orchestrator.ProcessMessageAsync(session, "I need a good laptop", CancellationToken.None);

        Assert.Equal("clarification", result.Type);
        Assert.Equal("What's your budget for this laptop?", result.Question);
        Assert.Null(result.Recommendation);
        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.NotNull(session.PendingClarification);
    }

    [Fact]
    public async Task The_orchestrator_passes_the_full_tool_catalog_to_the_chat_client_every_turn()
    {
        var session = new ConversationSession(Guid.NewGuid());
        var chatClient = new FakeChatClient("ok");
        var orchestrator = new ConversationOrchestrator(chatClient, new FakeToolCatalog(), new ToolResultCapture());

        await orchestrator.ProcessMessageAsync(session, "hello", CancellationToken.None);

        Assert.NotNull(chatClient.LastOptions);
        Assert.NotNull(chatClient.LastOptions!.Tools);
    }
}

using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Proves the infrastructure a follow-up question ("tell me more about the first one") needs to
/// resolve correctly: <see cref="ConversationSession.LastSearchResults"/> (FR-022, Phase 4.5 —
/// the generalized concept that superseded the originally-planned <c>LastRecommendation</c>) is
/// injected into the chat history as an exact id/name list before the route-specific
/// narration/tool call, so the language model can resolve an ordinal/descriptive reference to a
/// known id rather than guessing or re-deriving it from prior prose. Actually resolving natural
/// language to a position is the LLM's job and isn't asserted here (no live LLM in this test
/// project) — only that the context it needs is present.
/// </summary>
public class FollowUpQuestionTests
{
    [Fact]
    public async Task A_session_with_prior_search_results_injects_their_ids_into_the_chat_history()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        session.SetLastSearchResults(
        [
            new SearchResultReference(firstId, "Galaxy S24"),
            new SearchResultReference(secondId, "Pixel 9"),
        ]);

        var chatClient = new FakeChatClient(
            ExtractionJson.ProductFact(["the first one"]),
            "Sure, here's more about the Galaxy S24.");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestLogger.Instance);

        await orchestrator.ProcessMessageAsync(session, "Tell me more about the first one", CancellationToken.None);

        // LastMessages reflects the route-specific (narration/tool) call, the second of the
        // turn's two chat-client calls — extraction itself does not need this context.
        var contextMessage = Assert.Single(chatClient.LastMessages!,
            m => m.Role == ChatRole.System && m.Text.Contains(firstId.ToString(), StringComparison.Ordinal));
        Assert.Contains("Galaxy S24", contextMessage.Text, StringComparison.Ordinal);
        Assert.Contains(secondId.ToString(), contextMessage.Text, StringComparison.Ordinal);
        Assert.Contains("Pixel 9", contextMessage.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_with_no_prior_search_results_injects_no_such_context_message()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var chatClient = new FakeChatClient(
            ExtractionJson.ProductFact(["the first one"]),
            "What are you looking for?");
        var orchestrator = new ConversationOrchestrator(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestLogger.Instance);

        await orchestrator.ProcessMessageAsync(session, "the first one, please", CancellationToken.None);

        Assert.DoesNotContain(chatClient.LastMessages!,
            m => m.Role == ChatRole.System && m.Text.Contains("most recently shown products", StringComparison.OrdinalIgnoreCase));
    }
}

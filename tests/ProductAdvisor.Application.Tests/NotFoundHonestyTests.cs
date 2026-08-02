using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Proves the US3 "not found" acceptance scenario at the orchestration layer: when
/// <c>get_product_details</c> resolves to <c>{ found: false }</c> — a data-access tool result
/// that, unlike a compute tool's output, is never routed through <see cref="IToolResultCapture"/>
/// — the LLM's own "not found" narration is relayed as plain text and the turn carries no
/// invented <see cref="Recommendation"/> or <see cref="Comparison"/>, regardless of what the
/// narration says (research.md §1: the orchestrator never fabricates a structured result).
/// </summary>
public class NotFoundHonestyTests
{
    [Fact]
    public async Task A_not_found_narration_with_no_captured_tool_result_produces_no_invented_recommendation_or_comparison()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var capture = new ToolResultCapture();

        var orchestrator = new ConversationOrchestrator(
            new FakeChatClient("I couldn't find that product in our catalog."), new FakeToolCatalog(), capture);

        var result = await orchestrator.ProcessMessageAsync(
            session, "Is the Nokia 3310 Pro in stock?", CancellationToken.None);

        Assert.Equal("clarification", result.Type);
        Assert.Equal("I couldn't find that product in our catalog.", result.Question);
        Assert.Null(result.Recommendation);
        Assert.Null(result.Comparison);
    }

    [Fact]
    public async Task A_not_found_narration_does_not_advance_the_session_into_recommending_or_comparing_state()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        var orchestrator = new ConversationOrchestrator(
            new FakeChatClient("That product doesn't exist in our catalog."), new FakeToolCatalog(), new ToolResultCapture());

        await orchestrator.ProcessMessageAsync(session, "Tell me about the XYZ9000", CancellationToken.None);

        Assert.Equal(ConversationState.Collecting, session.State);
        Assert.Empty(session.LastSearchResults);
    }
}

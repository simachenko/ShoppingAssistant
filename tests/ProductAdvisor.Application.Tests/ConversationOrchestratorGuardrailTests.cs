using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 12: the guardrail/PII stages as actually wired into the turn-processing cycle — an
/// oversized or PII-blocked message never reaches extraction; a redacted message is what gets
/// persisted and sent onward, never the original raw text; an oversized requirementPatch is
/// rejected after extraction, before it's merged into session state.
/// </summary>
public class ConversationOrchestratorGuardrailTests
{
    private static ConversationOrchestrator BuildOrchestrator(FakeChatClient chatClient, RequestGuardrailOptions guardrailOptions) =>
        new(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            TestBudgetGuard.Generous, guardrailOptions, TestTurnMetrics.Instance, TestLogger.Instance);

    [Fact]
    public async Task An_oversized_message_never_reaches_extraction()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Smalltalk());
        var orchestrator = BuildOrchestrator(chatClient, new RequestGuardrailOptions { MaxMessageLength = 10 });
        var session = new ConversationSession(Guid.NewGuid(), "test-user");

        await Assert.ThrowsAsync<GuardrailRejectionException>(
            () => orchestrator.ProcessMessageAsync(session, "this message is far too long", CancellationToken.None));

        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task A_blocked_pii_message_never_reaches_extraction()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Smalltalk());
        var orchestrator = BuildOrchestrator(chatClient, new RequestGuardrailOptions());
        var session = new ConversationSession(Guid.NewGuid(), "test-user");

        await Assert.ThrowsAsync<GuardrailRejectionException>(
            () => orchestrator.ProcessMessageAsync(session, "My card is 4111 1111 1111 1111", CancellationToken.None));

        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task A_redacted_message_is_what_extraction_receives_never_the_original_raw_text()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Smalltalk(), "Hi there!");
        var orchestrator = BuildOrchestrator(chatClient, new RequestGuardrailOptions());
        var session = new ConversationSession(Guid.NewGuid(), "test-user");

        await orchestrator.ProcessMessageAsync(session, "Reach me at jane.doe@example.com please", CancellationToken.None);

        var extractionCallMessages = chatClient.CallHistory[0];
        Assert.DoesNotContain(extractionCallMessages, m => m.Text.Contains("jane.doe@example.com", StringComparison.Ordinal));
        Assert.Contains(session.Messages, m => m.Role == "user" && !m.Text.Contains("jane.doe@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_oversized_requirement_patch_is_rejected_after_extraction_before_merge()
    {
        var chatClient = new FakeChatClient(ExtractionJson.RequirementPatchOnly(
            category: "smartphones",
            requiredFeatures: ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u"]));
        var orchestrator = BuildOrchestrator(chatClient, new RequestGuardrailOptions { MaxListEntries = 20 });
        var session = new ConversationSession(Guid.NewGuid(), "test-user");

        await Assert.ThrowsAsync<GuardrailRejectionException>(
            () => orchestrator.ProcessMessageAsync(session, "I need a smartphone with many features", CancellationToken.None));

        // Rejected before merge — the oversized patch never became part of session state.
        Assert.Empty(session.CurrentRequirement.RequiredFeatures);
    }
}

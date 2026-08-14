using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Multilingual behaviour (spec.md 002 FR-029–FR-032). The first test covers a defect that
/// actually shipped: retrieval used the language detected for the message while narration used the
/// session's requirement language, which defaults to English and is rarely updated by a
/// store-policy turn — so a Ukrainian question retrieved Ukrainian content and answered in English.
/// </summary>
public class StoreInfoMultilingualTests
{
    private static ConversationOrchestrator BuildOrchestrator(
        FakeChatClient chatClient, IStoreInfoRetrievalService retrieval) =>
        new(
            chatClient, new FakeToolCatalog(), new ToolResultCapture(),
            new ExtractionStage(chatClient, TestTurnMetrics.Instance),
            new FakeRecommendationService(new Recommendation { RecommendationId = Guid.NewGuid(), Items = [] }),
            retrieval,
            TestBudgetGuard.Generous, new RequestGuardrailOptions(), TestTurnMetrics.Instance, TestLogger.Instance);

    [Fact]
    public async Task A_ukrainian_question_is_answered_in_ukrainian_even_though_the_session_defaults_to_english()
    {
        var session = new ConversationSession(Guid.NewGuid(), "test-user");
        Assert.Equal("en", session.CurrentRequirement.Language); // the default that used to win

        var retrieval = new FakeStoreInfoRetrievalService(StoreInfoAnswer.Empty);
        var chatClient = new FakeChatClient(ExtractionJson.StoreInfo(language: "uk"));
        var orchestrator = BuildOrchestrator(chatClient, retrieval);

        var result = await orchestrator.ProcessMessageAsync(
            session, "Чи можна повернути товар?", CancellationToken.None);

        // The deterministic not-found answer is an answer too (FR-029) — it must be Ukrainian.
        Assert.Equal(StoreInfoMessages.NotFound("uk"), result.Message);
        Assert.NotEqual(StoreInfoMessages.NotFound("en"), result.Message);
        Assert.Equal("uk", retrieval.LastLanguage);
    }

    [Fact]
    public async Task The_narration_call_is_told_to_reply_in_the_shoppers_language_not_the_documents()
    {
        var retrieval = new FakeStoreInfoRetrievalService(FakeStoreInfoRetrievalService.AnswerWith(
            "Standard delivery within Kyiv takes 1-2 business days.", language: "en"));
        var chatClient = new FakeChatClient(
            ExtractionJson.StoreInfo(language: "uk"),
            "Доставка в межах Києва триває 1-2 робочі дні.");
        var orchestrator = BuildOrchestrator(chatClient, retrieval);

        await orchestrator.ProcessMessageAsync(
            new ConversationSession(Guid.NewGuid(), "test-user"),
            "Скільки триває доставка?",
            CancellationToken.None);

        // FR-029: an English source document must not drag the reply into English. LastMessages is
        // the narration call (extraction ran first), so its system prompt is the instruction the
        // model actually received.
        var narrationPrompt = string.Join(
            "\n", chatClient.LastMessages!.Where(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Select(m => m.Text));
        Assert.Contains("Respond in this language: uk", narrationPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ungrounded_narration_falls_back_in_the_shoppers_language()
    {
        var answer = FakeStoreInfoRetrievalService.AnswerWith("Повернення протягом 14 календарних днів.", language: "uk");
        var envelope = EvidenceEnvelopeBuilder.ForStoreInfo(answer, "uk");

        var validated = OutputValidationStage.Validate("Повернення протягом 30 днів.", envelope);

        Assert.Equal(StoreInfoMessages.SeeCitedDocuments("uk"), validated);
        Assert.DoesNotContain("30", validated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("uk", "uk-UA")]
    [InlineData("uk-UA", "uk")]
    [InlineData("UK", "uk")]
    [InlineData("en-GB", "en_US")]
    public void Region_qualified_tags_are_the_same_language(string left, string right)
    {
        // FR-030 — without this a `uk-UA` shopper silently loses the same-language preference.
        Assert.True(LanguageTag.SameLanguage(left, right));
    }

    [Theory]
    [InlineData("uk", "en")]
    [InlineData("pl-PL", "uk-UA")]
    public void Genuinely_different_languages_do_not_match(string left, string right)
    {
        Assert.False(LanguageTag.SameLanguage(left, right));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_language_falls_back_to_the_default_rather_than_throwing(string? tag)
    {
        // A malformed language is a reason to fall back, never a reason to fail the shopper's turn.
        Assert.Equal(LanguageTag.Default, LanguageTag.Normalize(tag));
    }

    [Fact]
    public void A_language_with_no_fixed_translation_falls_back_to_the_default_wording()
    {
        // Falling back is a poor experience; inventing a translation would be a correctness
        // failure, so the default wording is the honest choice (FR-032).
        Assert.False(StoreInfoMessages.IsLocalized("pl"));
        Assert.Equal(StoreInfoMessages.NotFound("en"), StoreInfoMessages.NotFound("pl"));
    }

    [Fact]
    public void Ukrainian_is_a_localized_language_for_the_deterministic_messages()
    {
        Assert.True(StoreInfoMessages.IsLocalized("uk"));
        Assert.True(StoreInfoMessages.IsLocalized("uk-UA"));
    }
}

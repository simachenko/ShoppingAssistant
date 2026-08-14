namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The fixed, deterministic wording a store-policy turn falls back to (spec.md 002
/// FR-009/FR-010). Held in one place so the orchestrator's zero-evidence short circuit and output
/// validation's ungrounded-narration fallback can never drift into saying different things about
/// the same situation.
/// </summary>
/// <remarks>
/// Localized rather than translated at runtime: these messages are produced precisely when the
/// system has nothing it can honestly say, so spending a language-model call on them would
/// reintroduce the very thing they exist to avoid. A language with no entry falls back to
/// <see cref="LanguageTag.Default"/> — an answer in the wrong language is a poor experience, but a
/// silently *invented* one would be a correctness failure (FR-029/FR-032).
/// </remarks>
public static class StoreInfoMessages
{
    private static readonly Dictionary<string, string> NotFoundByLanguage = new(StringComparer.Ordinal)
    {
        ["en"] = "I couldn't find that in our store's reference material, so I don't want to guess. "
               + "You may want to check with our support team for this one.",
        ["uk"] = "Я не знайшов цього в довідкових матеріалах магазину, тому не хочу вгадувати. "
               + "Краще уточніть це в нашій службі підтримки.",
    };

    private static readonly Dictionary<string, string> SeeCitedDocumentsByLanguage = new(StringComparer.Ordinal)
    {
        ["en"] = "I found this in our store's reference material — please see the source document(s) "
               + "below for the exact details.",
        ["uk"] = "Я знайшов це в довідкових матеріалах магазину — точні деталі дивіться в джерелі "
               + "(документах) нижче.",
    };

    private static readonly Dictionary<string, string> ProductQuestionNotAnsweredByLanguage = new(StringComparer.Ordinal)
    {
        ["en"] = "\n\nYou also asked about a product — ask me that on its own and I'll look up the "
               + "live details for you.",
        ["uk"] = "\n\nВи також запитали про товар — запитайте про нього окремо, і я перевірю "
               + "актуальні дані.",
    };

    /// <summary>
    /// Used when retrieval returned no evidence at all. Produced without a language-model call —
    /// there is nothing for a model to summarize, and the only honest output is this statement, so
    /// spending a call on it would risk it being phrased as something less honest.
    /// </summary>
    public static string NotFound(string? language = null) => Localize(NotFoundByLanguage, language);

    /// <summary>
    /// Used when evidence exists but the narration built from it failed grounding validation — the
    /// documents are still cited, so the shopper can read the source directly rather than being
    /// handed a claim the system could not verify.
    /// </summary>
    public static string SeeCitedDocuments(string? language = null) =>
        Localize(SeeCitedDocumentsByLanguage, language);

    /// <summary>
    /// Appended when one message also asked a product question (spec.md 002 FR-006). The turn
    /// still answers only its primary intent — this exists so the other half is acknowledged and
    /// invited back, rather than silently dropped, which is the outcome FR-006 forbids.
    /// </summary>
    public static string ProductQuestionNotAnswered(string? language = null) =>
        Localize(ProductQuestionNotAnsweredByLanguage, language);

    /// <summary>True when a fixed message is available in the shopper's language (FR-029).</summary>
    public static bool IsLocalized(string? language) =>
        NotFoundByLanguage.ContainsKey(LanguageTag.Normalize(language));

    private static string Localize(Dictionary<string, string> byLanguage, string? language) =>
        byLanguage.TryGetValue(LanguageTag.Normalize(language), out var message)
            ? message
            : byLanguage[LanguageTag.Default];
}

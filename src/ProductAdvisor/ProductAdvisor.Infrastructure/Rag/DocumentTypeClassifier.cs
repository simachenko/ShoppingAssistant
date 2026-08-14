using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Rag;

/// <summary>
/// Guesses which policy area a question is about, to bias retrieval ranking toward documents of
/// that type (spec.md 002 FR-022, research.md §5).
/// </summary>
/// <remarks>
/// Deliberately a deterministic keyword lookup rather than a language-model call. Two reasons:
/// a `store_info` turn must stay within the same two-primary-call budget every other route has
/// (FR-071–FR-079), and a wrong guess here costs only ranking position — whereas a wrong guess
/// from a model asked to pick a type could not be reviewed or corrected without re-prompting.
/// When nothing matches, the result is <c>null</c>: retrieval then searches across all types
/// rather than forcing an incorrect preference, which is exactly what FR-022 requires.
/// </remarks>
public static class DocumentTypeClassifier
{
    // Ukrainian terms are included alongside English because the deployment's shoppers ask in
    // either language (FR-021) while the seeded knowledge base may be in only one of them.
    private static readonly (DocumentType Type, string[] Keywords)[] Keywords =
    [
        (DocumentType.Delivery, ["deliver", "delivery", "shipping", "ship", "courier", "dispatch", "доставк", "відправ", "кур'єр"]),
        (DocumentType.Payment, ["pay", "payment", "card", "instalment", "installment", "cash on delivery", "оплат", "плат", "картк", "розстрочк"]),
        (DocumentType.Returns, ["return", "refund", "exchange", "send it back", "поверн", "обмін"]),
        (DocumentType.Warranty, ["warranty", "guarantee", "repair", "service centre", "service center", "defect", "гарант", "ремонт"]),
        (DocumentType.Loyalty, ["loyalty", "bonus", "points", "reward", "tier", "лояльн", "бонус", "бал"]),
        (DocumentType.Contacts, ["contact", "phone", "email", "address", "support team", "open", "hours", "контакт", "телефон", "адрес", "пошт"]),
    ];

    /// <summary>
    /// Returns the best-matching type, or <c>null</c> when the question does not clearly belong to
    /// one — never a default type, which would silently bias every unclassifiable question.
    /// </summary>
    public static DocumentType? Classify(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        DocumentType? best = null;
        var bestScore = 0;

        foreach (var (type, keywords) in Keywords)
        {
            var score = keywords.Count(k => question.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (score > bestScore)
            {
                bestScore = score;
                best = type;
            }
        }

        return best;
    }
}

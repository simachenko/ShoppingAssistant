using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>Pure mapping from domain/turn-result types to the conversation API's wire shapes.</summary>
public static class ConversationApiMapper
{
    public static ConversationTurnResponse ToResponse(AdvisorTurnResult result) => result.Type switch
    {
        "clarification" => new ConversationTurnResponse("clarification", null, result.Question, null, null),
        "recommendation" => new ConversationTurnResponse(
            "recommendation",
            result.Message,
            null,
            result.Recommendation!.Items.Select(ToItemResponse).ToList(),
            result.Recommendation.UnmetConstraintExplanation,
            NearestAlternatives: result.Recommendation.NearestAlternatives.Select(ToNearestAlternativeResponse).ToList()),
        "comparison" => new ConversationTurnResponse(
            "comparison",
            result.Message,
            null,
            null,
            null,
            result.Comparison!.Criteria,
            result.Comparison.Rows.Select(ToRowResponse).ToList()),
        "checkoutLink" => new ConversationTurnResponse(
            "checkoutLink",
            result.Message,
            null,
            null,
            null,
            Url: result.CheckoutLink!.Url,
            ProductIds: result.CheckoutLink.ProductIds),
        // Citations ride along only when the turn actually produced them (a `store_info` turn) —
        // a `smalltalk` answer maps to the identical shape with the field absent (spec.md 002
        // FR-008, contracts/advisor-conversation-api-additions.md).
        "answer" => new ConversationTurnResponse(
            "answer", result.Message, null, null, null,
            Citations: result.Citations?.Select(ToCitationResponse).ToList()),
        "unsupported" => new ConversationTurnResponse("unsupported", result.Message, null, null, null),
        "error" => new ConversationTurnResponse("error", result.Message, null, null, null, Degraded: result.Degraded),
        _ => throw new NotSupportedException($"Unknown turn result type '{result.Type}'."),
    };

    /// <summary>
    /// Shared by the conversational path above and the direct <c>POST /api/comparisons</c>
    /// endpoint, so both ever produce the criteria/rows shape the same way (SC-010).
    /// </summary>
    public static (IReadOnlyList<string> Criteria, IReadOnlyList<ComparisonRowResponse> Rows) ToComparisonParts(Comparison comparison) =>
        (comparison.Criteria, comparison.Rows.Select(ToRowResponse).ToList());

    public static ConversationSnapshotResponse ToSnapshot(ConversationSession session) => new(
        session.SessionId,
        session.Messages.Select(m => new ConversationMessageResponse(m.Role, m.Text, m.Timestamp)).ToList(),
        new RequirementSnapshotResponse(
            session.CurrentRequirement.Category,
            session.CurrentRequirement.Budget is null
                ? null
                : new MoneyResponse(session.CurrentRequirement.Budget.Amount, session.CurrentRequirement.Budget.Currency),
            session.CurrentRequirement.RequiredFeatures,
            session.CurrentRequirement.Preferences));

    private static CitationResponse ToCitationResponse(Citation citation) =>
        new(citation.DocumentId, citation.DocumentTitle, citation.ChunkId);

    private static RecommendedItemResponse ToItemResponse(RecommendedItem item) => new(
        item.Candidate.ProductId,
        item.Candidate.Name,
        item.Candidate.Price is null ? null : new MoneyResponse(item.Candidate.Price.Amount, item.Candidate.Price.Currency),
        item.Candidate.PriceVerified,
        item.Candidate.Availability?.ToString(),
        item.Candidate.AvailabilityVerified,
        item.MatchedRequirements,
        item.TradeOffs,
        item.Score);

    private static NearestAlternativeResponse ToNearestAlternativeResponse(NearestAlternative alternative) => new(
        alternative.Candidate.ProductId,
        alternative.Candidate.Name,
        alternative.ViolatedConstraints);

    private static ComparisonRowResponse ToRowResponse(ComparisonRow row) => new(
        row.Candidate.ProductId,
        row.Candidate.Name,
        row.ValuesByCriterion,
        row.Rating,
        row.DeltasVsBest);
}

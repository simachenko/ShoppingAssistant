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
            result.Recommendation.UnmetConstraintExplanation),
        _ => throw new NotSupportedException($"Unknown turn result type '{result.Type}'."),
    };

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
}

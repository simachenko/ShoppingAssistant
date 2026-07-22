namespace ProductAdvisor.Application.Contracts;

// Mirrors contracts/advisor-conversation-api.md. "type" discriminates clarification vs
// recommendation vs comparison. All structured fields here are copied verbatim from the
// underlying tool result — "message"/"question" is the only LLM-authored text.
public sealed record ConversationTurnResponse(
    string Type,
    string? Message,
    string? Question,
    IReadOnlyList<RecommendedItemResponse>? Items,
    string? UnmetConstraintExplanation,
    IReadOnlyList<string>? Criteria = null,
    IReadOnlyList<ComparisonRowResponse>? Rows = null);

public sealed record ComparisonRowResponse(
    Guid ProductId,
    string Name,
    IReadOnlyDictionary<string, string?> Values,
    decimal Rating,
    IReadOnlyDictionary<string, string> DeltasVsBest);

public sealed record RecommendedItemResponse(
    Guid ProductId,
    string Name,
    MoneyResponse? Price,
    bool PriceVerified,
    string? Availability,
    bool AvailabilityVerified,
    IReadOnlyList<string> MatchedRequirements,
    IReadOnlyList<string> TradeOffs,
    decimal Score);

public sealed record MoneyResponse(decimal Amount, string Currency);

public sealed record ConversationSnapshotResponse(
    Guid SessionId,
    IReadOnlyList<ConversationMessageResponse> Messages,
    RequirementSnapshotResponse CurrentRequirement);

public sealed record ConversationMessageResponse(string Role, string Text, DateTimeOffset Timestamp);

public sealed record RequirementSnapshotResponse(
    string? Category, MoneyResponse? Budget, IReadOnlyList<string> RequiredFeatures, IReadOnlyList<string> Preferences);

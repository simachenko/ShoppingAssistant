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
    IReadOnlyList<ComparisonRowResponse>? Rows = null,
    string? Url = null,
    IReadOnlyList<Guid>? ProductIds = null,
    IReadOnlyList<NearestAlternativeResponse>? NearestAlternatives = null,
    bool? Degraded = null,
    IReadOnlyList<CitationResponse>? Citations = null);

/// <summary>
/// A source document backing a store-policy answer (spec.md 002 FR-008,
/// contracts/advisor-conversation-api-additions.md). Structured provenance, not narration — it
/// stays visible even if <c>Message</c> is regenerated, the same split every other structured
/// field here already has.
/// </summary>
public sealed record CitationResponse(Guid DocumentId, string DocumentTitle, Guid ChunkId);

public sealed record NearestAlternativeResponse(
    Guid ProductId,
    string Name,
    IReadOnlyList<string> ViolatedConstraints);

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

/// <summary>Request body for the stateless, non-conversational <c>POST /api/comparisons</c> (FR-018).</summary>
public sealed record DirectComparisonRequest(IReadOnlyList<string> ProductIds, bool IncludeExplanation = true);

/// <summary>
/// Response for <c>POST /api/comparisons</c> — <see cref="Criteria"/>/<see cref="Rows"/> come
/// from the same shared computation the conversational path uses (SC-010); <see cref="Explanation"/>
/// is `null` when not requested or when the separate, constrained narration call fails (FR-019).
/// </summary>
public sealed record DirectComparisonResponse(
    IReadOnlyList<string> Criteria, IReadOnlyList<ComparisonRowResponse> Rows, string? Explanation);

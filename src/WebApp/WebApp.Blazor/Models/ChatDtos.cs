namespace WebApp.Blazor.Models;

// Blazor's own copies of the Gateway's chat wire shapes (contracts/gateway-bff-api.md,
// contracts/advisor-conversation-api.md) — the UI depends only on the documented JSON contract.
public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record RecommendedItemDto(
    Guid ProductId,
    string Name,
    MoneyDto? Price,
    bool PriceVerified,
    string? Availability,
    bool AvailabilityVerified,
    IReadOnlyList<string> MatchedRequirements,
    IReadOnlyList<string> TradeOffs,
    decimal Score);

public sealed record ComparisonRowDto(
    Guid ProductId,
    string Name,
    IReadOnlyDictionary<string, string?> Values,
    decimal Rating,
    IReadOnlyDictionary<string, string> DeltasVsBest);

public sealed record ChatTurnDto(
    Guid SessionId,
    string Type,
    string? Message,
    string? Question,
    IReadOnlyList<RecommendedItemDto>? Items,
    string? UnmetConstraintExplanation,
    IReadOnlyList<string>? Criteria = null,
    IReadOnlyList<ComparisonRowDto>? Rows = null);

/// <summary>
/// One item from the Gateway's streaming chat endpoint (FR-015/research.md §11) — either a
/// narration-text delta (<see cref="Delta"/> set) or, exactly once and always last, the same
/// <see cref="ChatTurnDto"/> the non-streaming endpoint would have returned for this turn
/// (<see cref="Result"/> set).
/// </summary>
public sealed record ChatStreamEvent(string? Delta, ChatTurnDto? Result);

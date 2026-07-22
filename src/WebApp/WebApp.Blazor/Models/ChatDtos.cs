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

public sealed record ChatTurnDto(
    Guid SessionId,
    string Type,
    string? Message,
    string? Question,
    IReadOnlyList<RecommendedItemDto>? Items,
    string? UnmetConstraintExplanation);

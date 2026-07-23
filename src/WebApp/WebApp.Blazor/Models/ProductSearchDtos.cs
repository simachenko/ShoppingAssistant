namespace WebApp.Blazor.Models;

// Blazor's own copies of the Gateway's product-picker wire shapes (contracts/gateway-bff-api.md)
// — this flow never touches /api/chat/* or a sessionId.
public sealed record SpecificationDto(string Key, string Value, string? Unit);

public sealed record ProductCandidateDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    IReadOnlyList<SpecificationDto> Specifications,
    MoneyDto? Price,
    bool PriceVerified,
    string? Availability,
    bool AvailabilityVerified);

public sealed record ComparisonResultDto(
    IReadOnlyList<string> Criteria,
    IReadOnlyList<ComparisonRowDto> Rows,
    string? Explanation);

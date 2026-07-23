using Gateway.Api.Clients;

namespace Gateway.Api;

/// <summary>Merged Catalog+Pricing shape for the product-picker search results (data-model.md's
/// ProductCandidate) — assembled per-request, never persisted.</summary>
public sealed record ProductCandidateDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    IReadOnlyList<CatalogSpecificationDto> Specifications,
    PricingMoneyDto? Price,
    bool PriceVerified,
    string? Availability,
    bool AvailabilityVerified);

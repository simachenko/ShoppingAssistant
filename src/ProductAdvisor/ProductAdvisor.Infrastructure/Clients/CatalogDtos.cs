namespace ProductAdvisor.Infrastructure.Clients;

// Advisor's own copies of Catalog's wire shapes (contracts/catalog-api.md) — bounded contexts
// don't share C# types across service boundaries, only the documented JSON contract.
public sealed record CatalogSpecificationDto(string Key, string Value, string? Unit);

public sealed record CatalogProductDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    Guid CategoryId,
    IReadOnlyList<CatalogSpecificationDto> Specifications);

public sealed record CatalogSearchResponse(IReadOnlyList<CatalogProductDto> Items, int Page, int PageSize, int TotalCount);

public sealed record CatalogCategoryDto(Guid CategoryId, string Name, IReadOnlyList<string> ComparableAttributeKeys);

/// <summary>
/// A structured characteristic condition (FR-020) — used both as a tool-call argument (from the
/// LLM) and, unchanged, as part of the outgoing request to Catalog's parametric search endpoint.
/// `Operator` is one of "eq"/"gte"/"lte"/"between", matching Catalog's wire contract exactly.
/// </summary>
public sealed record CatalogCharacteristicFilterDto(string Key, string Operator, string Value, string? ValueTo = null);

public sealed record CatalogSearchRequest(
    Guid? CategoryId,
    string? Category,
    string? Query,
    IReadOnlyList<CatalogCharacteristicFilterDto>? Characteristics,
    int Page = 1,
    // Catalog's parametric search endpoint rejects any PageSize over its own MaxPageSize (100)
    // with a 400 — this must never exceed that cap, or every search_products call fails.
    int PageSize = 100);

/// <summary>
/// One <c>search_products</c> result item — specifications always present; price/availability
/// present (and verified) only when the search composed a Pricing lookup (a price range or sort
/// was requested), never guessed otherwise.
/// </summary>
public sealed record SearchResultItemDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    IReadOnlyList<CatalogSpecificationDto> Specifications,
    PricingMoneyDto? Price,
    bool PriceVerified,
    string? Availability,
    bool AvailabilityVerified);

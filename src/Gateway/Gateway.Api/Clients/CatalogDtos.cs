namespace Gateway.Api.Clients;

// Gateway's own copies of Catalog's wire shapes (contracts/catalog-api.md) — bounded contexts
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

/// <summary>
/// A structured characteristic condition (FR-020) — `Operator` is one of "eq"/"gte"/"lte"/
/// "between", matching Catalog's wire contract exactly.
/// </summary>
public sealed record CatalogCharacteristicFilterDto(string Key, string Operator, string Value, string? ValueTo = null);

public sealed record CatalogSearchRequest(
    Guid? CategoryId,
    string? Category,
    string? Query,
    IReadOnlyList<CatalogCharacteristicFilterDto>? Characteristics,
    int Page = 1,
    int PageSize = 100);

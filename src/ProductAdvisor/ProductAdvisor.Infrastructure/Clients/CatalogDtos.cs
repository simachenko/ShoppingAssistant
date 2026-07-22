namespace ProductAdvisor.Infrastructure.Clients;

// Advisor's own copies of Catalog's wire shapes (contracts/catalog-api.md) — bounded contexts
// don't share C# types across service boundaries, only the documented JSON contract.
public sealed record CatalogSpecificationDto(string Key, string Value, string? Unit);

public sealed record CatalogProductDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    IReadOnlyList<CatalogSpecificationDto> Specifications);

public sealed record CatalogSearchResponse(IReadOnlyList<CatalogProductDto> Items, int Page, int PageSize, int TotalCount);

public sealed record CatalogCategoryDto(Guid CategoryId, string Name, IReadOnlyList<string> ComparableAttributeKeys);

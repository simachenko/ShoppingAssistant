namespace ProductCatalog.Application.Contracts;

/// <summary>Request body for the parametric search endpoint (FR-020, contracts/catalog-api.md).</summary>
public sealed record ProductSearchRequest(
    Guid? CategoryId = null,
    string? Category = null,
    string? Query = null,
    IReadOnlyList<CharacteristicFilter>? Characteristics = null,
    int Page = 1,
    int PageSize = ProductCatalogService.DefaultPageSize);

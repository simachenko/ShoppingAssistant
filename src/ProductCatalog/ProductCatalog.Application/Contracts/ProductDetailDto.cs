namespace ProductCatalog.Application.Contracts;

public sealed record ProductDetailDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    string Description,
    bool IsActive,
    IReadOnlyList<SpecificationDto> Specifications);

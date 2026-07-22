namespace ProductCatalog.Application.Contracts;

public sealed record ProductDetailDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    Guid CategoryId,
    string Description,
    bool IsActive,
    IReadOnlyList<SpecificationDto> Specifications);

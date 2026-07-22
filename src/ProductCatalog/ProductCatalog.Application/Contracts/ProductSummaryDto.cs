namespace ProductCatalog.Application.Contracts;

public sealed record ProductSummaryDto(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    Guid CategoryId,
    IReadOnlyList<SpecificationDto> Specifications);

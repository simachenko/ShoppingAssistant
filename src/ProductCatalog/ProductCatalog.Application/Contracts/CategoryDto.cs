namespace ProductCatalog.Application.Contracts;

public sealed record CategoryDto(Guid CategoryId, string Name, IReadOnlyList<string> ComparableAttributeKeys);

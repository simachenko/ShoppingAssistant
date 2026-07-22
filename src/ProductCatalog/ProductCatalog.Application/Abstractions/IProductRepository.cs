using ProductCatalog.Domain;

namespace ProductCatalog.Application.Abstractions;

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? category, string? query, int page, int pageSize, CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<Brand?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Brand>> GetBrandsAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken);

    Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Category>> GetCategoriesAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    Task<Category?> FindCategoryByNameAsync(string name, CancellationToken cancellationToken);
}

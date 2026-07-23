using ProductCatalog.Domain;

namespace ProductCatalog.Application.Abstractions;

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? category, string? query, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Unpaginated category/free-text-narrowed candidate set for the parametric search (FR-020)
    /// — characteristic filtering runs in-process, after this, in
    /// <see cref="ProductCatalogService"/> (research.md §13), so pagination must apply to the
    /// post-filter result, not this one.
    /// </summary>
    Task<IReadOnlyList<Product>> SearchAllAsync(
        Guid? categoryId, string? category, string? query, CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<Brand?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Brand>> GetBrandsAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken);

    Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Category>> GetCategoriesAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    Task<Category?> FindCategoryByNameAsync(string name, CancellationToken cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure.Repositories;

public sealed class ProductRepository(CatalogDbContext dbContext) : IProductRepository
{
    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? category, string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var productsQuery = dbContext.Products.Where(p => p.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
        {
            // Case-insensitive: the LLM may pass "smartphones" for "Smartphones" — same
            // tolerance already applied to the free-text query below.
            var matchingCategoryIds = await dbContext.Categories
                .Where(c => EF.Functions.ILike(c.Name, category))
                .Select(c => c.CategoryId)
                .ToListAsync(cancellationToken);

            productsQuery = productsQuery.Where(p => matchingCategoryIds.Contains(p.CategoryId));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            productsQuery = productsQuery.Where(p =>
                EF.Functions.ILike(p.Name, $"%{query}%") || EF.Functions.ILike(p.Description, $"%{query}%"));
        }

        var totalCount = await productsQuery.CountAsync(cancellationToken);

        var items = await productsQuery
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Products.FirstOrDefaultAsync(p => p.ProductId == productId, cancellationToken);

    public Task<Brand?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken) =>
        dbContext.Brands.FirstOrDefaultAsync(b => b.BrandId == brandId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Brand>> GetBrandsAsync(
        IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken)
    {
        var brands = await dbContext.Brands.Where(b => brandIds.Contains(b.BrandId)).ToListAsync(cancellationToken);
        return brands.ToDictionary(b => b.BrandId);
    }

    public Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.Categories.FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Category>> GetCategoriesAsync(
        IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories.Where(c => categoryIds.Contains(c.CategoryId)).ToListAsync(cancellationToken);
        return categories.ToDictionary(c => c.CategoryId);
    }

    public Task<Category?> FindCategoryByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Categories.FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, name), cancellationToken);
}

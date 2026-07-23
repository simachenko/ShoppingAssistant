using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure.Repositories;

public sealed class ProductRepository(CatalogDbContext dbContext) : IProductRepository
{
    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? category, string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var productsQuery = await BuildFilteredQueryAsync(null, category, query, cancellationToken);

        var totalCount = await productsQuery.CountAsync(cancellationToken);

        var items = await productsQuery
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Product>> SearchAllAsync(
        Guid? categoryId, string? category, string? query, CancellationToken cancellationToken)
    {
        var productsQuery = await BuildFilteredQueryAsync(categoryId, category, query, cancellationToken);
        return await productsQuery.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    private async Task<IQueryable<Product>> BuildFilteredQueryAsync(
        Guid? categoryId, string? category, string? query, CancellationToken cancellationToken)
    {
        var productsQuery = dbContext.Products.Where(p => p.IsActive).AsQueryable();

        if (categoryId is not null)
        {
            productsQuery = productsQuery.Where(p => p.CategoryId == categoryId);
        }
        else if (!string.IsNullOrWhiteSpace(category))
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
            // The LLM often passes the user's own phrasing verbatim, which may combine brand and
            // model in ways a single "%query%" substring never matches (e.g. "Samsung Galaxy S24"
            // when the product name alone is "Galaxy S24", or "GooglePixel 9" with no space at
            // all). Two independent, deliberately generous strategies, either of which is enough:
            // (1) every whitespace-separated word of the query appears somewhere across name,
            // description, or brand — order- and field-independent; (2) the whole query, with
            // whitespace stripped, is a substring of the brand+name with whitespace stripped too
            // — catches brand-plus-model phrases regardless of spacing.
            // Pre-format each token into its "%word%" pattern here (client-side) rather than
            // inside the query lambda — EF Core cannot translate string interpolation/Format
            // evaluated per-element of a captured array inside a correlated predicate.
            var tokenPatterns = query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => $"%{token}%")
                .ToList();
            var queryNoSpacesPattern = $"%{new string(query.Where(c => !char.IsWhiteSpace(c)).ToArray())}%";

            productsQuery =
                from p in productsQuery
                join b in dbContext.Brands on p.BrandId equals b.BrandId
                where
                    tokenPatterns.All(pattern =>
                        EF.Functions.ILike(p.Name, pattern) ||
                        EF.Functions.ILike(p.Description, pattern) ||
                        EF.Functions.ILike(b.Name, pattern))
                    || EF.Functions.ILike((b.Name + p.Name).Replace(" ", ""), queryNoSpacesPattern)
                select p;
        }

        return productsQuery;
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

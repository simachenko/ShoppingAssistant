using ProductCatalog.Application.Abstractions;
using ProductCatalog.Application.Contracts;
using ProductCatalog.Domain;

namespace ProductCatalog.Application;

/// <summary>Coordinates the Catalog use cases (search, detail lookup, category lookup).</summary>
public sealed class ProductCatalogService(IProductRepository repository)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<ProductSummaryDto>> SearchProductsAsync(
        string? category, string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
        if (pageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"PageSize must be between 1 and {MaxPageSize}.");

        var (items, totalCount) = await repository.SearchAsync(category, query, page, pageSize, cancellationToken);

        var brandIds = items.Select(p => p.BrandId).Distinct().ToArray();
        var categoryIds = items.Select(p => p.CategoryId).Distinct().ToArray();
        var brands = await repository.GetBrandsAsync(brandIds, cancellationToken);
        var categories = await repository.GetCategoriesAsync(categoryIds, cancellationToken);

        var summaries = items
            .Select(p => ToSummary(p, brands.GetValueOrDefault(p.BrandId)?.Name ?? "", categories.GetValueOrDefault(p.CategoryId)?.Name ?? ""))
            .ToList();

        return new PagedResult<ProductSummaryDto>(summaries, page, pageSize, totalCount);
    }

    public async Task<ProductDetailDto?> GetProductDetailAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var brand = await repository.GetBrandAsync(product.BrandId, cancellationToken);
        var category = await repository.GetCategoryAsync(product.CategoryId, cancellationToken);

        return new ProductDetailDto(
            product.ProductId,
            product.Name,
            brand?.Name ?? "",
            category?.Name ?? "",
            product.Description,
            product.IsActive,
            product.Specifications.Select(ToSpecDto).ToList());
    }

    public async Task<CategoryDto?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        return category is null ? null : ToCategoryDto(category);
    }

    private static ProductSummaryDto ToSummary(Product product, string brandName, string categoryName) =>
        new(product.ProductId, product.Name, brandName, categoryName, product.Specifications.Select(ToSpecDto).ToList());

    private static SpecificationDto ToSpecDto(Specification spec) => new(spec.Key, spec.Value, spec.Unit);

    private static CategoryDto ToCategoryDto(Category category) =>
        new(category.CategoryId, category.Name, category.ComparableAttributeKeys);
}

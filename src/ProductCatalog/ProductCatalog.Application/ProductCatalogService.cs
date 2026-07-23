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
            product.CategoryId,
            product.Description,
            product.IsActive,
            product.Specifications.Select(ToSpecDto).ToList());
    }

    public async Task<CategoryDto?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        return category is null ? null : ToCategoryDto(category);
    }

    /// <summary>Resolves a category's identity and comparable characteristics by name (FR-021).</summary>
    public async Task<CategoryDto?> GetCategoryByNameAsync(string name, CancellationToken cancellationToken)
    {
        var category = await repository.FindCategoryByNameAsync(name, cancellationToken);
        return category is null ? null : ToCategoryDto(category);
    }

    /// <summary>
    /// Parametric search (FR-020): category/free-text narrowing is pushed to SQL
    /// (<see cref="IProductRepository.SearchAllAsync"/>); characteristic filters are then
    /// evaluated in-process on that already-narrowed set (research.md §13) before this method
    /// paginates the final, filtered result — pagination MUST apply after filtering, not before,
    /// or a later page could silently miss matches that were excluded by an earlier page's cut.
    /// </summary>
    public async Task<PagedResult<ProductSummaryDto>> SearchProductsAdvancedAsync(
        ProductSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Page must be >= 1.");
        if (request.PageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(request), $"PageSize must be between 1 and {MaxPageSize}.");

        var characteristics = request.Characteristics ?? [];
        foreach (var filter in characteristics)
        {
            if (filter.Operator == CharacteristicFilterOperator.Between && filter.ValueTo is null)
            {
                throw new ArgumentException(
                    $"Characteristic filter '{filter.Key}' uses operator 'between' and requires valueTo.", nameof(request));
            }
        }

        var candidates = await repository.SearchAllAsync(
            request.CategoryId, request.Category, request.Query, cancellationToken);

        var filtered = characteristics.Count == 0
            ? candidates
            : candidates.Where(p => CharacteristicFilterMatcher.MatchesAll(p, characteristics)).ToList();

        var totalCount = filtered.Count;
        var pageItems = filtered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();

        var brandIds = pageItems.Select(p => p.BrandId).Distinct().ToArray();
        var categoryIds = pageItems.Select(p => p.CategoryId).Distinct().ToArray();
        var brands = await repository.GetBrandsAsync(brandIds, cancellationToken);
        var categories = await repository.GetCategoriesAsync(categoryIds, cancellationToken);

        var summaries = pageItems
            .Select(p => ToSummary(p, brands.GetValueOrDefault(p.BrandId)?.Name ?? "", categories.GetValueOrDefault(p.CategoryId)?.Name ?? ""))
            .ToList();

        return new PagedResult<ProductSummaryDto>(summaries, request.Page, request.PageSize, totalCount);
    }

    private static ProductSummaryDto ToSummary(Product product, string brandName, string categoryName) =>
        new(product.ProductId, product.Name, brandName, categoryName, product.CategoryId,
            product.Specifications.Select(ToSpecDto).ToList());

    private static SpecificationDto ToSpecDto(Specification spec) => new(spec.Key, spec.Value, spec.Unit);

    private static CategoryDto ToCategoryDto(Category category) =>
        new(category.CategoryId, category.Name, category.ComparableAttributeKeys);
}

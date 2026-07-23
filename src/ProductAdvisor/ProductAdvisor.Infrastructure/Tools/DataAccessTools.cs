using System.ComponentModel;
using ModelContextProtocol.Server;
using ProductAdvisor.Infrastructure.Clients;

namespace ProductAdvisor.Infrastructure.Tools;

/// <summary>
/// Data-access-only MCP tools: they call straight through to Catalog/Pricing and never filter,
/// score, or compare (contracts/advisor-mcp-tools.md). The LLM may only call these to gather
/// grounded facts — the actual computation tools (get_recommendations, compare_products) live
/// alongside <see cref="ComputeTools"/>.
/// </summary>
[McpServerToolType]
public sealed class DataAccessTools(CatalogClient catalogClient, PricingClient pricingClient)
{
    [McpServerTool(Name = "search_products", UseStructuredContent = true)]
    [Description("Search the retailer's catalog for products in a category, optionally matching a free-text query, a price range, and structured characteristic conditions (e.g., camera resolution at least 48 MP). Returns product identity, specifications, and — when a price range or sort is given — verified price/availability. Do not filter, sort, or rank the results yourself; every condition you can express here is applied deterministically by this tool.")]
    public async Task<IReadOnlyList<SearchResultItemDto>> SearchProductsAsync(
        [Description("Product category name, e.g. 'Smartphones'")] string? category = null,
        [Description("Category id (guid), if already known, e.g. from get_category")] string? categoryId = null,
        [Description("Optional free-text keywords")] string? query = null,
        [Description("Structured characteristic conditions")] IReadOnlyList<CatalogCharacteristicFilterDto>? characteristics = null,
        [Description("Minimum price")] decimal? priceMin = null,
        [Description("Maximum price")] decimal? priceMax = null,
        [Description("Sort order: price_asc, price_desc, or name")] string? sortBy = null,
        [Description("Max results to return, e.g. 10 for 'top 10 phones'")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var parsedCategoryId = Guid.TryParse(categoryId, out var id) ? id : (Guid?)null;
        var searchRequest = new CatalogSearchRequest(parsedCategoryId, category, query, characteristics);
        var searchResult = await catalogClient.SearchAdvancedAsync(searchRequest, cancellationToken);

        var needsPricing = priceMin is not null || priceMax is not null || sortBy is not null;
        if (!needsPricing)
        {
            IEnumerable<SearchResultItemDto> plainItems = searchResult.Items.Select(ToUnverifiedItem);
            return ApplyLimit(plainItems, limit).ToList();
        }

        var offers = searchResult.Items.Count == 0
            ? new PricingBatchResponse([], [])
            : await pricingClient.GetOffersAsync(searchResult.Items.Select(p => p.ProductId).ToList(), cancellationToken);
        var offersByProduct = offers.Offers.ToDictionary(o => o.ProductId);

        IEnumerable<SearchResultItemDto> withPricing = searchResult.Items.Select(p => ToPricedItem(p, offersByProduct));

        if (priceMin is not null)
        {
            withPricing = withPricing.Where(p => p.PriceVerified && p.Price!.Amount >= priceMin);
        }

        if (priceMax is not null)
        {
            withPricing = withPricing.Where(p => p.PriceVerified && p.Price!.Amount <= priceMax);
        }

        withPricing = sortBy switch
        {
            "price_asc" => withPricing.Where(p => p.PriceVerified).OrderBy(p => p.Price!.Amount),
            "price_desc" => withPricing.Where(p => p.PriceVerified).OrderByDescending(p => p.Price!.Amount),
            "name" => withPricing.OrderBy(p => p.Name, StringComparer.Ordinal),
            _ => withPricing,
        };

        return ApplyLimit(withPricing, limit).ToList();
    }

    [McpServerTool(Name = "get_category", UseStructuredContent = true)]
    [Description("Resolve a product category's identity and its comparable characteristics, by name or by id. Use this before searching or comparing by a characteristic you're not sure is spelled/named exactly right in the catalog.")]
    public async Task<object> GetCategoryAsync(
        [Description("Category name")] string? name = null,
        [Description("Category id (guid), if already known")] string? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        CatalogCategoryDto? category = null;
        if (Guid.TryParse(categoryId, out var id))
        {
            category = await catalogClient.GetCategoryAsync(id, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            category = await catalogClient.GetCategoryByNameAsync(name, cancellationToken);
        }

        return category is null ? new { found = false } : new { found = true, category };
    }

    private static SearchResultItemDto ToUnverifiedItem(CatalogProductDto product) => new(
        product.ProductId, product.Name, product.Brand, product.Category, product.Specifications,
        Price: null, PriceVerified: false, Availability: null, AvailabilityVerified: false);

    private static SearchResultItemDto ToPricedItem(CatalogProductDto product, Dictionary<Guid, PricingOfferDto> offersByProduct)
    {
        offersByProduct.TryGetValue(product.ProductId, out var offer);
        return new SearchResultItemDto(
            product.ProductId, product.Name, product.Brand, product.Category, product.Specifications,
            Price: offer?.Price, PriceVerified: offer is not null,
            Availability: offer?.Availability, AvailabilityVerified: offer is not null);
    }

    private static IEnumerable<SearchResultItemDto> ApplyLimit(IEnumerable<SearchResultItemDto> items, int? limit) =>
        limit is > 0 ? items.Take(limit.Value) : items;

    [McpServerTool(Name = "get_product_details", UseStructuredContent = true)]
    [Description("Look up a single product's identity and specifications by id. Returns { found: false } if the product does not exist — never a fabricated record.")]
    public async Task<object> GetProductDetailsAsync(
        [Description("Product id (guid)")] string productId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(productId, out var id))
        {
            return new { found = false };
        }

        var product = await catalogClient.GetProductDetailAsync(id, cancellationToken);
        return product is null ? new { found = false } : new { found = true, product };
    }

    [McpServerTool(Name = "check_price_and_availability", UseStructuredContent = true)]
    [Description("Check current price and stock availability for up to 50 product ids in one call. Ids with no pricing record appear in notFound rather than being guessed.")]
    public async Task<PricingBatchResponse> CheckPriceAndAvailabilityAsync(
        [Description("Product ids to check (max 50)")] IReadOnlyList<string> productIds,
        CancellationToken cancellationToken = default)
    {
        if (productIds.Count == 0)
        {
            throw new ArgumentException("At least one productId is required.", nameof(productIds));
        }

        if (productIds.Count > 50)
        {
            throw new ArgumentException("At most 50 productIds are allowed per call.", nameof(productIds));
        }

        var ids = productIds.Select(Guid.Parse).ToList();
        return await pricingClient.GetOffersAsync(ids, cancellationToken);
    }
}

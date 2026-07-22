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
    [Description("Search the retailer's catalog for products in a category, optionally matching a free-text query. Returns product identity and specifications only — no price or stock.")]
    public async Task<IReadOnlyList<CatalogProductDto>> SearchProductsAsync(
        [Description("Product category, e.g. 'Smartphones'")] string category,
        [Description("Optional free-text keywords")] string? query = null,
        CancellationToken cancellationToken = default) =>
        await catalogClient.SearchProductsAsync(category, query, cancellationToken);

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

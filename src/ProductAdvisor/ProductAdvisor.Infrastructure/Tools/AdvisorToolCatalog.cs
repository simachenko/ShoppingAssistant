using Microsoft.Extensions.AI;
using ProductAdvisor.Application;

namespace ProductAdvisor.Infrastructure.Tools;

/// <summary>
/// Wraps the same deterministic tool-handler methods hosted over true MCP transport
/// (<see cref="DataAccessTools"/>, <see cref="ComputeTools"/>) as in-process
/// <see cref="AIFunction"/>s for the chat client's function-invocation loop — no separate
/// implementation, no separate behavior, just a different invocation path (research.md §1).
/// </summary>
public sealed class AdvisorToolCatalog(DataAccessTools dataAccessTools, ComputeTools computeTools) : IAdvisorToolCatalog
{
    public IReadOnlyList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(
            dataAccessTools.SearchProductsAsync,
            "search_products",
            "Search the retailer's catalog for products in a category, optionally matching a free-text query. Returns product identity and specifications only — no price or stock."),
        AIFunctionFactory.Create(
            dataAccessTools.GetProductDetailsAsync,
            "get_product_details",
            "Look up a single product's identity and specifications by id. Returns { found: false } if the product does not exist — never a fabricated record."),
        AIFunctionFactory.Create(
            dataAccessTools.CheckPriceAndAvailabilityAsync,
            "check_price_and_availability",
            "Check current price and stock availability for up to 50 product ids in one call. Ids with no pricing record appear in notFound rather than being guessed."),
        AIFunctionFactory.Create(
            computeTools.GetRecommendationsAsync,
            "get_recommendations",
            "Given a fully-specified need (category, budget, required features, preferences), return a ranked, deterministically scored set of matching products with pre-computed match reasons and trade-offs — or an explanation of why nothing matches. Do not attempt to filter, rank, or score candidates yourself; always call this tool once category and budget are known."),
    ];
}

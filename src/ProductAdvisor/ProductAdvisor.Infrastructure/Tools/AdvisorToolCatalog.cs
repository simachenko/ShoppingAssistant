using Microsoft.Extensions.AI;
using ProductAdvisor.Application;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Infrastructure.ToolRecipes;

namespace ProductAdvisor.Infrastructure.Tools;

/// <summary>
/// Wraps the same deterministic tool-handler methods hosted over true MCP transport
/// (<see cref="DataAccessTools"/>, <see cref="ComputeTools"/>) as in-process
/// <see cref="AIFunction"/>s for the chat client's function-invocation loop — no separate
/// implementation, no separate behavior, just a different invocation path (research.md §1).
/// </summary>
public sealed class AdvisorToolCatalog(
    DataAccessTools dataAccessTools, ComputeTools computeTools, RagTools ragTools) : IAdvisorToolCatalog
{
    public IReadOnlyList<AITool> GetTools() =>
    [
        AIFunctionFactory.Create(
            dataAccessTools.SearchProductsAsync,
            "search_products",
            "Search the retailer's catalog for products in a category, optionally matching a free-text query, a price range, and structured characteristic conditions (e.g., camera resolution at least 48 MP). Returns product identity, specifications, and — when a price range or sort is given — verified price/availability. Do not filter, sort, or rank the results yourself; every condition you can express here is applied deterministically by this tool."),
        AIFunctionFactory.Create(
            dataAccessTools.GetCategoryAsync,
            "get_category",
            "Resolve a product category's identity and its comparable characteristics, by name or by id. Use this before searching or comparing by a characteristic you're not sure is spelled/named exactly right in the catalog."),
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
        AIFunctionFactory.Create(
            computeTools.CompareProductsAsync,
            "compare_products",
            "Given two or more product ids, return their specifications side-by-side using one shared set of criteria, plus a deterministic rating per product and computed deltas versus the best value in the set for each criterion. Do not compute comparisons, ratings, or differences yourself — always call this tool and only elaborate on its output."),
        AIFunctionFactory.Create(
            computeTools.GenerateCheckoutLinkAsync,
            "generate_checkout_link",
            "Given one or more product ids the user wants to buy — resolved from their names or from an ordinal/descriptive reference to the most recently shown results — return a checkout link listing exactly those products. Do not construct the link yourself; always call this tool, and if you cannot resolve which products the user means, ask rather than guessing."),
        AIFunctionFactory.Create(
            ragTools.RetrieveStoreInfoAsync,
            "retrieve_store_info",
            "Search the store's reference documentation (delivery, payment, returns, warranty, loyalty program, contacts, and other store policies) for content relevant to a shopper's question. Returns matched fragments with their source document, or an empty result when nothing in the knowledge base is relevant enough to answer confidently. Never used for product price, availability, specifications, or comparisons — those come only from the product-data tools in this catalog."),
    ];

    public IReadOnlyList<AITool> GetTools(Route route)
    {
        var allowed = ToolRecipe.GetAllowedToolNames(route);
        return [.. GetTools().Where(t => allowed.Contains(t.Name))];
    }
}

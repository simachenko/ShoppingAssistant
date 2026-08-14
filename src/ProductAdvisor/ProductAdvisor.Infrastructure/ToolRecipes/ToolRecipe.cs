using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Infrastructure.ToolRecipes;

/// <summary>
/// The fixed, minimal set of tool names each policy-routing route may invoke this turn
/// (spec.md FR-066–FR-070, data-model.md `ToolRecipe`) — a tool outside the current route's
/// recipe is never reachable for that turn (FR-068). Only the three routes still bridged through
/// the free tool-invocation loop (`compare`/`checkout`/`product_fact`,
/// <c>ConversationOrchestrator</c>) need a non-empty entry here: `recommend` and `store_info`
/// already call their terminal tool deterministically (<c>IRecommendationService</c>,
/// <c>IStoreInfoRetrievalService</c>), and `smalltalk`/`unsupported`/`clarify` make zero tool
/// calls. `store_info`'s empty entry is also what structurally denies it every product-data tool
/// (spec.md 002 FR-004) — and no product route's entry names <c>retrieve_store_info</c>, denying
/// the reverse direction just as structurally (spec.md 002 FR-005).
/// </summary>
public static class ToolRecipe
{
    public static IReadOnlySet<string> GetAllowedToolNames(Route route) => route switch
    {
        Route.ProductFact => ProductFactTools,
        Route.Compare => CompareTools,
        Route.Checkout => CheckoutTools,
        _ => EmptyTools,
    };

    private static readonly IReadOnlySet<string> ProductFactTools =
        new HashSet<string> { "search_products", "get_product_details", "check_price_and_availability" };

    private static readonly IReadOnlySet<string> CompareTools =
        new HashSet<string> { "search_products", "compare_products" };

    private static readonly IReadOnlySet<string> CheckoutTools =
        new HashSet<string> { "search_products", "generate_checkout_link" };

    private static readonly IReadOnlySet<string> EmptyTools = new HashSet<string>();
}

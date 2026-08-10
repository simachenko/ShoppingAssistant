using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Infrastructure.ToolRecipes;

/// <summary>
/// The fixed, minimal set of tool names each policy-routing route may invoke this turn
/// (spec.md FR-066–FR-070, data-model.md `ToolRecipe`) — a tool outside the current route's
/// recipe is never reachable for that turn (FR-068). Only the three routes still bridged through
/// the free tool-invocation loop (`compare`/`checkout`/`product_fact`,
/// <c>ConversationOrchestrator</c>) need a non-empty entry here: `recommend` already calls its
/// terminal tool deterministically (Phase 9, <c>IRecommendationService</c>), and
/// `smalltalk`/`unsupported`/`clarify` make zero tool calls.
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

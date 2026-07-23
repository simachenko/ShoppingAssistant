using System.ComponentModel;
using ModelContextProtocol.Server;
using ProductAdvisor.Application;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Clients;

namespace ProductAdvisor.Infrastructure.Tools;

/// <summary>
/// Deterministic compute tools: filtering, budget validation, scoring, and (later) comparison
/// ratings/deltas all happen only inside these tool handlers — never in the conversation
/// orchestration loop (research.md §1, plan.md Summary). The LLM may only call these tools and
/// narrate their already-computed output.
/// </summary>
[McpServerToolType]
public sealed class ComputeTools(
    CatalogClient catalogClient,
    PricingClient pricingClient,
    IToolResultCapture resultCapture,
    ProductComparisonService comparisonService)
{
    [McpServerTool(Name = "get_recommendations", UseStructuredContent = true)]
    [Description("Given a fully-specified need (category, budget, required features, preferences), return a ranked, deterministically scored set of matching products with pre-computed match reasons and trade-offs — or an explanation of why nothing matches. Do not attempt to filter, rank, or score candidates yourself; always call this tool once category and budget are known.")]
    public async Task<Recommendation> GetRecommendationsAsync(
        [Description("Product category, e.g. 'Smartphones'")] string category,
        [Description("Maximum budget amount")] decimal budgetAmount,
        [Description("Budget currency, ISO 4217, e.g. UAH")] string budgetCurrency,
        [Description("Required features/specs, free text, e.g. 'camera_mp'")] IReadOnlyList<string>? requiredFeatures = null,
        [Description("Soft preferences, free text")] IReadOnlyList<string>? preferences = null,
        CancellationToken cancellationToken = default)
    {
        var requirement = new UserRequirement
        {
            Category = category,
            Budget = new Money(budgetAmount, budgetCurrency),
            RequiredFeatures = requiredFeatures ?? [],
            Preferences = preferences ?? [],
        };

        var products = await catalogClient.SearchProductsAsync(category, null, cancellationToken);

        var offers = products.Count == 0
            ? new PricingBatchResponse([], [])
            : await pricingClient.GetOffersAsync(products.Select(p => p.ProductId).ToList(), cancellationToken);

        var candidates = ProductCandidateBuilder.Build(products, offers);
        var recommendation = ScoringPolicy.Score(requirement, candidates);

        resultCapture.SetRecommendation(recommendation, requirement);
        return recommendation;
    }

    [McpServerTool(Name = "compare_products", UseStructuredContent = true)]
    [Description("Given two or more product ids, return their specifications side-by-side using one shared set of criteria, plus a deterministic rating per product and computed deltas versus the best value in the set for each criterion. Do not compute comparisons, ratings, or differences yourself — always call this tool and only elaborate on its output.")]
    public async Task<Comparison> CompareProductsAsync(
        [Description("Two to ten product ids (guid) to compare")] IReadOnlyList<string> productIds,
        CancellationToken cancellationToken = default)
    {
        var comparison = await comparisonService.CompareAsync(productIds, cancellationToken);
        resultCapture.SetComparison(comparison);
        return comparison;
    }
}

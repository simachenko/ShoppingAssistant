using System.ComponentModel;
using Microsoft.Extensions.Configuration;
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
    ProductComparisonService comparisonService,
    IConfiguration configuration)
{
    [McpServerTool(Name = "get_recommendations", UseStructuredContent = true)]
    [Description("Given a fully-specified need (category, budget, required features, preferences), return a ranked, deterministically scored set of matching products with pre-computed match reasons and trade-offs — or an explanation of why nothing matches. Do not attempt to filter, rank, or score candidates yourself; always call this tool once category and budget are known.")]
    public Task<Recommendation> GetRecommendationsAsync(
        [Description("Product category, e.g. 'Smartphones'")] string category,
        [Description("Maximum budget amount")] decimal budgetAmount,
        [Description("Budget currency, ISO 4217, e.g. UAH")] string budgetCurrency,
        [Description("Required features/specs, free text, e.g. 'camera_mp'")] IReadOnlyList<string>? requiredFeatures = null,
        [Description("Soft preferences, free text")] IReadOnlyList<string>? preferences = null,
        [Description("Availability requirements the user explicitly stated, e.g. 'ships this week'; when non-empty, out-of-stock candidates are hard-excluded (FR-085)")] IReadOnlyList<string>? availabilityRequirements = null,
        CancellationToken cancellationToken = default) =>
        GetRecommendationsFromRequirementAsync(
            new UserRequirement
            {
                Category = category,
                Budget = new Money(budgetAmount, budgetCurrency),
                RequiredFeatures = requiredFeatures ?? [],
                Preferences = preferences ?? [],
                AvailabilityRequirements = availabilityRequirements ?? [],
            },
            cancellationToken);

    /// <summary>
    /// The shared computation both the LLM-facing tool above and the deterministic `recommend`
    /// route (<see cref="ProductAdvisor.Application.Pipeline.IRecommendationService"/>, spec.md
    /// FR-066) call — never two independent implementations of the same recommendation logic.
    /// </summary>
    public async Task<Recommendation> GetRecommendationsFromRequirementAsync(
        UserRequirement requirement, CancellationToken cancellationToken = default)
    {
        if (!requirement.HasEssentialInformation)
        {
            throw new ArgumentException(
                "Category and Budget must both be known before a recommendation can be computed.",
                nameof(requirement));
        }

        var products = await catalogClient.SearchProductsAsync(requirement.Category!, null, cancellationToken);

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

    [McpServerTool(Name = "generate_checkout_link", UseStructuredContent = true)]
    [Description("Given one or more product ids the user wants to buy — resolved from their names or from an ordinal/descriptive reference to the most recently shown results — return a checkout link listing exactly those products. Do not construct the link yourself; always call this tool, and if you cannot resolve which products the user means, ask rather than guessing.")]
    public async Task<CheckoutLink> GenerateCheckoutLinkAsync(
        [Description("One or more product ids (guid) the user wants to buy")] IReadOnlyList<string> productIds,
        CancellationToken cancellationToken = default)
    {
        // FR-108: a malformed id is rejected rather than passed through (or crashing the call) —
        // treated the same as "not found" below, alongside any id that parses but doesn't
        // resolve to a real product.
        var ids = productIds
            .Select(id => Guid.TryParse(id, out var parsed) ? (Guid?)parsed : null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var details = await Task.WhenAll(ids.Select(id => catalogClient.GetProductDetailAsync(id, cancellationToken)));
        var resolvedIds = details.Where(d => d is not null).Select(d => d!.ProductId).ToList();

        if (resolvedIds.Count == 0)
        {
            throw new InvalidOperationException(
                "None of the requested products could be found; nothing to check out.");
        }

        var baseUrl = configuration["CheckoutBaseUrl"] ?? "https://retailer.example/checkout";
        var url = $"{baseUrl}?productIds={string.Join(",", resolvedIds)}";

        var checkoutLink = new CheckoutLink { Url = url, ProductIds = resolvedIds };
        resultCapture.SetCheckoutLink(checkoutLink);
        return checkoutLink;
    }
}

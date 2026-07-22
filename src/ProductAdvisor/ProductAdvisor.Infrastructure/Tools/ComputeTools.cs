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
public sealed class ComputeTools(CatalogClient catalogClient, PricingClient pricingClient, IToolResultCapture resultCapture)
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

        var candidates = BuildCandidates(products, offers);
        var recommendation = ScoringPolicy.Score(requirement, candidates);

        resultCapture.SetRecommendation(recommendation, requirement);
        return recommendation;
    }

    private static List<ProductCandidate> BuildCandidates(
        IReadOnlyList<CatalogProductDto> products, PricingBatchResponse offers)
    {
        var offersByProduct = offers.Offers.ToDictionary(o => o.ProductId);

        return products.Select(p =>
        {
            offersByProduct.TryGetValue(p.ProductId, out var offer);

            return new ProductCandidate
            {
                ProductId = p.ProductId,
                Name = p.Name,
                BrandName = p.Brand,
                CategoryName = p.Category,
                Specifications = p.Specifications.Select(s => new Specification(s.Key, s.Value, s.Unit)).ToList(),
                Price = offer is null ? null : new Money(offer.Price.Amount, offer.Price.Currency),
                Availability = offer is null ? null : Enum.Parse<StockStatus>(offer.Availability),
                PriceVerified = offer is not null,
                AvailabilityVerified = offer is not null,
            };
        }).ToList();
    }
}

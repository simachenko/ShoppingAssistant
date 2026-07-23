using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Clients;

namespace ProductAdvisor.Infrastructure;

/// <summary>
/// The single implementation of "assemble candidates from Catalog+Pricing, then run
/// <see cref="ComparisonEngine"/>" — called from both the conversational <c>compare_products</c>
/// MCP tool handler (<see cref="Tools.ComputeTools"/>) and the stateless
/// <c>POST /api/comparisons</c> endpoint, so there is exactly one computation path and comparing
/// the same product-id set through either one yields byte-identical results (FR-018, SC-010,
/// research.md §14).
/// </summary>
public sealed class ProductComparisonService(CatalogClient catalogClient, PricingClient pricingClient)
{
    public async Task<Comparison> CompareAsync(IReadOnlyList<string> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count < 2)
        {
            throw new ArgumentException("At least two productIds are required to compare.", nameof(productIds));
        }

        if (productIds.Count > 10)
        {
            throw new ArgumentException("At most 10 productIds are allowed per call.", nameof(productIds));
        }

        var ids = productIds.Select(Guid.Parse).ToList();

        var details = await Task.WhenAll(ids.Select(id => catalogClient.GetProductDetailAsync(id, cancellationToken)));
        var foundProducts = details.Where(d => d is not null).Select(d => d!).ToList();

        if (foundProducts.Count < 2)
        {
            throw new InvalidOperationException(
                "Fewer than two of the requested products could be found; nothing to compare.");
        }

        // Criteria order/identity comes from the (first found product's) category's own
        // canonical ComparableAttributeKeys, never from whichever specs happen to be present on
        // the compared products — that's what guarantees FR-006/SC-002 (identical criteria, same
        // order every time), even when a product is missing a given spec.
        var category = await catalogClient.GetCategoryAsync(foundProducts[0].CategoryId, cancellationToken);
        var comparableAttributeKeys = category?.ComparableAttributeKeys ?? [];

        var offers = await pricingClient.GetOffersAsync(foundProducts.Select(p => p.ProductId).ToList(), cancellationToken);
        var candidates = ProductCandidateBuilder.Build(foundProducts, offers);

        return ComparisonEngine.Compare(candidates, comparableAttributeKeys);
    }
}

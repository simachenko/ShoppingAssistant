using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Clients;

namespace ProductAdvisor.Infrastructure;

/// <summary>
/// Joins Catalog's product data with Pricing's offer data into <see cref="ProductCandidate"/> —
/// the one place this join happens, reused by both the recommendation and comparison
/// compositions so there is exactly one mapping, never two that could drift.
/// </summary>
internal static class ProductCandidateBuilder
{
    public static List<ProductCandidate> Build(IReadOnlyList<CatalogProductDto> products, PricingBatchResponse offers)
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

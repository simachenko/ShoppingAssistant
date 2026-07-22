using PricingAvailability.Application.Abstractions;
using PricingAvailability.Application.Contracts;
using PricingAvailability.Domain;

namespace PricingAvailability.Application;

/// <summary>Coordinates the Pricing and Availability use cases (single and batch offer lookup).</summary>
public sealed class PricingService(IOfferRepository repository)
{
    public const int MaxBatchSize = 50;

    public async Task<OfferDto?> GetOfferAsync(Guid productId, CancellationToken cancellationToken)
    {
        var offer = await repository.GetByProductIdAsync(productId, cancellationToken);
        return offer is null ? null : ToDto(offer);
    }

    public async Task<BatchOffersResult> GetOffersAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            throw new ArgumentException("At least one productId is required.", nameof(productIds));
        if (productIds.Count > MaxBatchSize)
            throw new ArgumentException($"At most {MaxBatchSize} productIds are allowed per call.", nameof(productIds));

        var offers = await repository.GetByProductIdsAsync(productIds, cancellationToken);
        var found = offers.Select(o => o.ProductId).ToHashSet();
        var notFound = productIds.Where(id => !found.Contains(id)).ToList();

        return new BatchOffersResult(offers.Select(ToDto).ToList(), notFound);
    }

    private static OfferDto ToDto(Offer offer) => new(
        offer.ProductId,
        new MoneyDto(offer.Price.Amount, offer.Price.Currency),
        offer.Discount is null ? null : new DiscountDto(offer.Discount.PercentOff, offer.Discount.ValidUntil),
        offer.Availability.ToString(),
        offer.AsOf,
        offer.Source);
}

using PricingAvailability.Domain;

namespace PricingAvailability.Application.Abstractions;

public interface IOfferRepository
{
    Task<Offer?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Offer>> GetByProductIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

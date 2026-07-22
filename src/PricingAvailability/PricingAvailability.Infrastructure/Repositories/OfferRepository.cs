using Microsoft.EntityFrameworkCore;
using PricingAvailability.Application.Abstractions;
using PricingAvailability.Domain;

namespace PricingAvailability.Infrastructure.Repositories;

public sealed class OfferRepository(PricingDbContext dbContext) : IOfferRepository
{
    public Task<Offer?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Offers.FirstOrDefaultAsync(o => o.ProductId == productId, cancellationToken);

    public async Task<IReadOnlyList<Offer>> GetByProductIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        await dbContext.Offers
            .Where(o => productIds.Contains(o.ProductId))
            .ToListAsync(cancellationToken);
}

namespace PricingAvailability.Application.Contracts;

public sealed record BatchOffersResult(IReadOnlyList<OfferDto> Offers, IReadOnlyList<Guid> NotFound);

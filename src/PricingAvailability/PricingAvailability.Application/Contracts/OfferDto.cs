namespace PricingAvailability.Application.Contracts;

public sealed record OfferDto(
    Guid ProductId,
    MoneyDto Price,
    DiscountDto? Discount,
    string Availability,
    DateTimeOffset AsOf,
    string Source);

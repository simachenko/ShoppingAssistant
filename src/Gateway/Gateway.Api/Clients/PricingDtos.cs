namespace Gateway.Api.Clients;

// Gateway's own copies of Pricing's wire shapes (contracts/pricing-api.md).
public sealed record PricingMoneyDto(decimal Amount, string Currency);

public sealed record PricingDiscountDto(decimal PercentOff, DateTimeOffset? ValidUntil);

public sealed record PricingOfferDto(
    Guid ProductId,
    PricingMoneyDto Price,
    PricingDiscountDto? Discount,
    string Availability,
    DateTimeOffset AsOf,
    string Source);

public sealed record PricingBatchResponse(IReadOnlyList<PricingOfferDto> Offers, IReadOnlyList<Guid> NotFound);

namespace PricingAvailability.Application.Contracts;

public sealed record DiscountDto(decimal PercentOff, DateTimeOffset? ValidUntil);

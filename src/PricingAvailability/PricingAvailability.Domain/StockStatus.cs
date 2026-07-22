namespace PricingAvailability.Domain;

/// <summary>
/// <see cref="Unknown"/> is deliberately the default (0) value — availability must never be
/// guessed as <see cref="InStock"/> when the upstream source didn't confirm it (FR-005).
/// </summary>
public enum StockStatus
{
    Unknown = 0,
    InStock,
    LimitedStock,
    OutOfStock,
}

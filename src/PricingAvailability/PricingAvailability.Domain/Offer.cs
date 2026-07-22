namespace PricingAvailability.Domain;

/// <summary>Aggregate root for the Pricing and Availability bounded context.</summary>
public sealed class Offer
{
    public Guid OfferId { get; private set; }

    /// <summary>References Catalog's Product by id only — no foreign key, no cross-service query.</summary>
    public Guid ProductId { get; private set; }

    public Money Price { get; private set; } = null!;
    public Discount? Discount { get; private set; }
    public StockStatus Availability { get; private set; } = StockStatus.Unknown;
    public DateTimeOffset AsOf { get; private set; }
    public string Source { get; private set; } = string.Empty;

    public Offer(
        Guid offerId,
        Guid productId,
        Money price,
        DateTimeOffset asOf,
        string source,
        StockStatus availability = StockStatus.Unknown,
        Discount? discount = null)
    {
        if (offerId == Guid.Empty)
            throw new ArgumentException("OfferId is required.", nameof(offerId));
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId is required.", nameof(productId));
        ArgumentNullException.ThrowIfNull(price);
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required.", nameof(source));

        OfferId = offerId;
        ProductId = productId;
        Price = price;
        AsOf = asOf;
        Source = source;
        Availability = availability;
        Discount = discount;
    }

    private Offer()
    {
        // EF Core materialization only.
    }

    public void UpdatePrice(Money price, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(price);
        Price = price;
        AsOf = asOf;
    }

    public void UpdateAvailability(StockStatus availability, DateTimeOffset asOf)
    {
        Availability = availability;
        AsOf = asOf;
    }

    public void ApplyDiscount(Discount discount)
    {
        ArgumentNullException.ThrowIfNull(discount);
        Discount = discount;
    }

    public void ClearDiscount() => Discount = null;
}

namespace PricingAvailability.Domain;

/// <summary>Plain class (not a record) for the same EF Core constructor-binding reason as Money.</summary>
public sealed class Discount : IEquatable<Discount>
{
    public decimal PercentOff { get; }
    public DateTimeOffset? ValidUntil { get; }

    public Discount(decimal percentOff, DateTimeOffset? validUntil = null)
    {
        if (percentOff is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentOff), "PercentOff must be between 0 and 100.");

        PercentOff = percentOff;
        ValidUntil = validUntil;
    }

    public bool Equals(Discount? other) =>
        other is not null && PercentOff == other.PercentOff && ValidUntil == other.ValidUntil;

    public override bool Equals(object? obj) => Equals(obj as Discount);

    public override int GetHashCode() => HashCode.Combine(PercentOff, ValidUntil);
}

using PricingAvailability.Domain;

namespace TestSupport.SeedData;

/// <summary>
/// Offers correlated to <see cref="CatalogSeedData"/>'s products by id only (no FK, no
/// cross-service query — just matching guids), with deliberately varied
/// price/availability/discount so scenarios like "no match under budget" and "cannot verify
/// availability" have real data to exercise.
/// </summary>
public static class PricingSeedData
{
    public static readonly Guid GalaxyS24OfferId = Guid.Parse("00000000-0000-0000-0004-000000000001");
    public static readonly Guid Pixel9OfferId = Guid.Parse("00000000-0000-0000-0004-000000000002");
    public static readonly Guid IPhone15OfferId = Guid.Parse("00000000-0000-0000-0004-000000000003");
    public static readonly Guid Xps13OfferId = Guid.Parse("00000000-0000-0000-0004-000000000004");

    public static IReadOnlyList<Offer> Offers { get; } = BuildOffers();

    private static IReadOnlyList<Offer> BuildOffers()
    {
        var now = DateTimeOffset.UtcNow;

        var galaxyS24 = new Offer(
            GalaxyS24OfferId, CatalogSeedData.GalaxyS24Id,
            new Money(14500m, "UAH"), now, "seed", StockStatus.InStock);
        galaxyS24.ApplyDiscount(new Discount(10, now.AddDays(7)));

        var pixel9 = new Offer(
            Pixel9OfferId, CatalogSeedData.Pixel9Id,
            new Money(15800m, "UAH"), now, "seed", StockStatus.LimitedStock);

        var iphone15 = new Offer(
            IPhone15OfferId, CatalogSeedData.IPhone15Id,
            new Money(32000m, "UAH"), now, "seed", StockStatus.OutOfStock);

        // Availability deliberately left Unknown — the upstream feed hasn't confirmed stock for
        // this one, exercising the "cannot be verified" path (FR-005).
        var xps13 = new Offer(
            Xps13OfferId, CatalogSeedData.Xps13Id,
            new Money(45000m, "UAH"), now, "seed");

        return [galaxyS24, pixel9, iphone15, xps13];
    }
}

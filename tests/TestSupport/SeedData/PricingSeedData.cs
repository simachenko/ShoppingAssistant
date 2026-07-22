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
    public static readonly Guid Xiaomi14OfferId = Guid.Parse("00000000-0000-0000-0004-000000000005");
    public static readonly Guid OnePlus12OfferId = Guid.Parse("00000000-0000-0000-0004-000000000006");
    public static readonly Guid SurfaceLaptop6OfferId = Guid.Parse("00000000-0000-0000-0004-000000000007");
    public static readonly Guid ThinkPadX1CarbonOfferId = Guid.Parse("00000000-0000-0000-0004-000000000008");
    public static readonly Guid IPadAirOfferId = Guid.Parse("00000000-0000-0000-0004-000000000009");
    public static readonly Guid GalaxyTabS9OfferId = Guid.Parse("00000000-0000-0000-0004-00000000000a");
    public static readonly Guid XiaomiPad6OfferId = Guid.Parse("00000000-0000-0000-0004-00000000000b");
    public static readonly Guid SonyWh1000Xm5OfferId = Guid.Parse("00000000-0000-0000-0004-00000000000c");
    public static readonly Guid BoseQuietComfortUltraOfferId = Guid.Parse("00000000-0000-0000-0004-00000000000d");
    public static readonly Guid AirPodsMaxOfferId = Guid.Parse("00000000-0000-0000-0004-00000000000e");

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

        var xiaomi14 = new Offer(
            Xiaomi14OfferId, CatalogSeedData.Xiaomi14Id,
            new Money(15200m, "UAH"), now, "seed", StockStatus.InStock);

        var onePlus12 = new Offer(
            OnePlus12OfferId, CatalogSeedData.OnePlus12Id,
            new Money(16500m, "UAH"), now, "seed", StockStatus.InStock);
        onePlus12.ApplyDiscount(new Discount(5, now.AddDays(3)));

        var surfaceLaptop6 = new Offer(
            SurfaceLaptop6OfferId, CatalogSeedData.SurfaceLaptop6Id,
            new Money(42000m, "UAH"), now, "seed", StockStatus.LimitedStock);

        var thinkPadX1Carbon = new Offer(
            ThinkPadX1CarbonOfferId, CatalogSeedData.ThinkPadX1CarbonId,
            new Money(55000m, "UAH"), now, "seed", StockStatus.InStock);

        var ipadAir = new Offer(
            IPadAirOfferId, CatalogSeedData.IPadAirId,
            new Money(21000m, "UAH"), now, "seed", StockStatus.InStock);

        var galaxyTabS9 = new Offer(
            GalaxyTabS9OfferId, CatalogSeedData.GalaxyTabS9Id,
            new Money(24000m, "UAH"), now, "seed", StockStatus.OutOfStock);

        var xiaomiPad6 = new Offer(
            XiaomiPad6OfferId, CatalogSeedData.XiaomiPad6Id,
            new Money(12000m, "UAH"), now, "seed", StockStatus.InStock);

        var sonyWh1000Xm5 = new Offer(
            SonyWh1000Xm5OfferId, CatalogSeedData.SonyWh1000Xm5Id,
            new Money(11000m, "UAH"), now, "seed", StockStatus.InStock);
        sonyWh1000Xm5.ApplyDiscount(new Discount(15, now.AddDays(14)));

        var boseQuietComfortUltra = new Offer(
            BoseQuietComfortUltraOfferId, CatalogSeedData.BoseQuietComfortUltraId,
            new Money(13500m, "UAH"), now, "seed", StockStatus.LimitedStock);

        // Availability deliberately left Unknown — same "cannot be verified" path as xps13 above.
        var airPodsMax = new Offer(
            AirPodsMaxOfferId, CatalogSeedData.AirPodsMaxId,
            new Money(18000m, "UAH"), now, "seed");

        return
        [
            galaxyS24, pixel9, iphone15, xps13,
            xiaomi14, onePlus12, surfaceLaptop6, thinkPadX1Carbon,
            ipadAir, galaxyTabS9, xiaomiPad6,
            sonyWh1000Xm5, boseQuietComfortUltra, airPodsMax,
        ];
    }
}

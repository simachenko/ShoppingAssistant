using PricingAvailability.Domain;

namespace PricingAvailability.Infrastructure.SeedData;

/// <summary>
/// The same fixed demo dataset as <c>TestSupport.SeedData.PricingSeedData</c> (same guids,
/// correlated to <c>ProductCatalog.Infrastructure.SeedData.DemoSeedData</c>'s product ids),
/// duplicated here because production code must not reference a test project. Only inserted
/// when <c>SeedDemoData</c> is enabled and the table is empty (Program.cs).
/// </summary>
public static class DemoSeedData
{
    // Must match ProductCatalog.Infrastructure.SeedData.DemoSeedData's product ids.
    private static readonly Guid GalaxyS24Id = Guid.Parse("00000000-0000-0000-0003-000000000001");
    private static readonly Guid Pixel9Id = Guid.Parse("00000000-0000-0000-0003-000000000002");
    private static readonly Guid IPhone15Id = Guid.Parse("00000000-0000-0000-0003-000000000003");
    private static readonly Guid Xps13Id = Guid.Parse("00000000-0000-0000-0003-000000000004");
    private static readonly Guid Xiaomi14Id = Guid.Parse("00000000-0000-0000-0003-000000000005");
    private static readonly Guid OnePlus12Id = Guid.Parse("00000000-0000-0000-0003-000000000006");
    private static readonly Guid SurfaceLaptop6Id = Guid.Parse("00000000-0000-0000-0003-000000000007");
    private static readonly Guid ThinkPadX1CarbonId = Guid.Parse("00000000-0000-0000-0003-000000000008");
    private static readonly Guid IPadAirId = Guid.Parse("00000000-0000-0000-0003-000000000009");
    private static readonly Guid GalaxyTabS9Id = Guid.Parse("00000000-0000-0000-0003-00000000000a");
    private static readonly Guid XiaomiPad6Id = Guid.Parse("00000000-0000-0000-0003-00000000000b");
    private static readonly Guid SonyWh1000Xm5Id = Guid.Parse("00000000-0000-0000-0003-00000000000c");
    private static readonly Guid BoseQuietComfortUltraId = Guid.Parse("00000000-0000-0000-0003-00000000000d");
    private static readonly Guid AirPodsMaxId = Guid.Parse("00000000-0000-0000-0003-00000000000e");

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
            GalaxyS24OfferId, GalaxyS24Id,
            new Money(14500m, "UAH"), now, "seed", StockStatus.InStock);
        galaxyS24.ApplyDiscount(new Discount(10, now.AddDays(7)));

        var pixel9 = new Offer(
            Pixel9OfferId, Pixel9Id,
            new Money(15800m, "UAH"), now, "seed", StockStatus.LimitedStock);

        var iphone15 = new Offer(
            IPhone15OfferId, IPhone15Id,
            new Money(32000m, "UAH"), now, "seed", StockStatus.OutOfStock);

        // Availability deliberately left Unknown — exercises the "cannot be verified" path (FR-005).
        var xps13 = new Offer(
            Xps13OfferId, Xps13Id,
            new Money(45000m, "UAH"), now, "seed");

        var xiaomi14 = new Offer(
            Xiaomi14OfferId, Xiaomi14Id,
            new Money(15200m, "UAH"), now, "seed", StockStatus.InStock);

        var onePlus12 = new Offer(
            OnePlus12OfferId, OnePlus12Id,
            new Money(16500m, "UAH"), now, "seed", StockStatus.InStock);
        onePlus12.ApplyDiscount(new Discount(5, now.AddDays(3)));

        var surfaceLaptop6 = new Offer(
            SurfaceLaptop6OfferId, SurfaceLaptop6Id,
            new Money(42000m, "UAH"), now, "seed", StockStatus.LimitedStock);

        var thinkPadX1Carbon = new Offer(
            ThinkPadX1CarbonOfferId, ThinkPadX1CarbonId,
            new Money(55000m, "UAH"), now, "seed", StockStatus.InStock);

        var ipadAir = new Offer(
            IPadAirOfferId, IPadAirId,
            new Money(21000m, "UAH"), now, "seed", StockStatus.InStock);

        var galaxyTabS9 = new Offer(
            GalaxyTabS9OfferId, GalaxyTabS9Id,
            new Money(24000m, "UAH"), now, "seed", StockStatus.OutOfStock);

        var xiaomiPad6 = new Offer(
            XiaomiPad6OfferId, XiaomiPad6Id,
            new Money(12000m, "UAH"), now, "seed", StockStatus.InStock);

        var sonyWh1000Xm5 = new Offer(
            SonyWh1000Xm5OfferId, SonyWh1000Xm5Id,
            new Money(11000m, "UAH"), now, "seed", StockStatus.InStock);
        sonyWh1000Xm5.ApplyDiscount(new Discount(15, now.AddDays(14)));

        var boseQuietComfortUltra = new Offer(
            BoseQuietComfortUltraOfferId, BoseQuietComfortUltraId,
            new Money(13500m, "UAH"), now, "seed", StockStatus.LimitedStock);

        // Availability deliberately left Unknown — same "cannot be verified" path as xps13 above.
        var airPodsMax = new Offer(
            AirPodsMaxOfferId, AirPodsMaxId,
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

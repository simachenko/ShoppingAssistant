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

    public static readonly Guid GalaxyS24OfferId = Guid.Parse("00000000-0000-0000-0004-000000000001");
    public static readonly Guid Pixel9OfferId = Guid.Parse("00000000-0000-0000-0004-000000000002");
    public static readonly Guid IPhone15OfferId = Guid.Parse("00000000-0000-0000-0004-000000000003");
    public static readonly Guid Xps13OfferId = Guid.Parse("00000000-0000-0000-0004-000000000004");

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

        return [galaxyS24, pixel9, iphone15, xps13];
    }
}

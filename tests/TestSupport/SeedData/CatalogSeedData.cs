using ProductCatalog.Domain;

namespace TestSupport.SeedData;

/// <summary>
/// A small, fixed dataset spanning four categories, reused by contract/integration tests and by
/// the EndToEnd/quickstart scenarios so the same product ids/names are referenced everywhere
/// (e.g., "compare Galaxy S24 and Pixel 9"). Ids are fixed constants, not random, so scenarios
/// can name a specific product deterministically across test runs. Only ever append new
/// products/brands/categories here — existing ids/specs are asserted on by name elsewhere.
/// </summary>
public static class CatalogSeedData
{
    public static readonly Guid SmartphonesCategoryId = Guid.Parse("00000000-0000-0000-0001-000000000001");
    public static readonly Guid LaptopsCategoryId = Guid.Parse("00000000-0000-0000-0001-000000000002");
    public static readonly Guid TabletsCategoryId = Guid.Parse("00000000-0000-0000-0001-000000000003");
    public static readonly Guid HeadphonesCategoryId = Guid.Parse("00000000-0000-0000-0001-000000000004");

    public static readonly Guid SamsungBrandId = Guid.Parse("00000000-0000-0000-0002-000000000001");
    public static readonly Guid GoogleBrandId = Guid.Parse("00000000-0000-0000-0002-000000000002");
    public static readonly Guid AppleBrandId = Guid.Parse("00000000-0000-0000-0002-000000000003");
    public static readonly Guid DellBrandId = Guid.Parse("00000000-0000-0000-0002-000000000004");
    public static readonly Guid XiaomiBrandId = Guid.Parse("00000000-0000-0000-0002-000000000005");
    public static readonly Guid OnePlusBrandId = Guid.Parse("00000000-0000-0000-0002-000000000006");
    public static readonly Guid MicrosoftBrandId = Guid.Parse("00000000-0000-0000-0002-000000000007");
    public static readonly Guid LenovoBrandId = Guid.Parse("00000000-0000-0000-0002-000000000008");
    public static readonly Guid SonyBrandId = Guid.Parse("00000000-0000-0000-0002-000000000009");
    public static readonly Guid BoseBrandId = Guid.Parse("00000000-0000-0000-0002-00000000000a");

    public static readonly Guid GalaxyS24Id = Guid.Parse("00000000-0000-0000-0003-000000000001");
    public static readonly Guid Pixel9Id = Guid.Parse("00000000-0000-0000-0003-000000000002");
    public static readonly Guid IPhone15Id = Guid.Parse("00000000-0000-0000-0003-000000000003");
    public static readonly Guid Xps13Id = Guid.Parse("00000000-0000-0000-0003-000000000004");
    public static readonly Guid Xiaomi14Id = Guid.Parse("00000000-0000-0000-0003-000000000005");
    public static readonly Guid OnePlus12Id = Guid.Parse("00000000-0000-0000-0003-000000000006");
    public static readonly Guid SurfaceLaptop6Id = Guid.Parse("00000000-0000-0000-0003-000000000007");
    public static readonly Guid ThinkPadX1CarbonId = Guid.Parse("00000000-0000-0000-0003-000000000008");
    public static readonly Guid IPadAirId = Guid.Parse("00000000-0000-0000-0003-000000000009");
    public static readonly Guid GalaxyTabS9Id = Guid.Parse("00000000-0000-0000-0003-00000000000a");
    public static readonly Guid XiaomiPad6Id = Guid.Parse("00000000-0000-0000-0003-00000000000b");
    public static readonly Guid SonyWh1000Xm5Id = Guid.Parse("00000000-0000-0000-0003-00000000000c");
    public static readonly Guid BoseQuietComfortUltraId = Guid.Parse("00000000-0000-0000-0003-00000000000d");
    public static readonly Guid AirPodsMaxId = Guid.Parse("00000000-0000-0000-0003-00000000000e");

    public static IReadOnlyList<Category> Categories { get; } =
    [
        new(SmartphonesCategoryId, "Smartphones", ["camera_mp", "battery_mah", "storage_gb"]),
        new(LaptopsCategoryId, "Laptops", ["cpu", "ram_gb", "storage_gb"]),
        new(TabletsCategoryId, "Tablets", ["screen_in", "storage_gb", "battery_mah"]),
        new(HeadphonesCategoryId, "Headphones", ["battery_hours", "noise_cancelling", "weight_g"]),
    ];

    public static IReadOnlyList<Brand> Brands { get; } =
    [
        new(SamsungBrandId, "Samsung"),
        new(GoogleBrandId, "Google"),
        new(AppleBrandId, "Apple"),
        new(DellBrandId, "Dell"),
        new(XiaomiBrandId, "Xiaomi"),
        new(OnePlusBrandId, "OnePlus"),
        new(MicrosoftBrandId, "Microsoft"),
        new(LenovoBrandId, "Lenovo"),
        new(SonyBrandId, "Sony"),
        new(BoseBrandId, "Bose"),
    ];

    public static IReadOnlyList<Product> Products { get; } = BuildProducts();

    private static IReadOnlyList<Product> BuildProducts()
    {
        var galaxyS24 = new Product(GalaxyS24Id, "Galaxy S24", SamsungBrandId, SmartphonesCategoryId,
            "Samsung's flagship smartphone with a 50MP main camera.");
        galaxyS24.AddSpecification(new Specification("camera_mp", "50", "MP"));
        galaxyS24.AddSpecification(new Specification("battery_mah", "4000", "mAh"));
        galaxyS24.AddSpecification(new Specification("storage_gb", "256", "GB"));
        galaxyS24.Activate();

        var pixel9 = new Product(Pixel9Id, "Pixel 9", GoogleBrandId, SmartphonesCategoryId,
            "Google's flagship smartphone with a large battery.");
        pixel9.AddSpecification(new Specification("camera_mp", "50", "MP"));
        pixel9.AddSpecification(new Specification("battery_mah", "4700", "mAh"));
        pixel9.AddSpecification(new Specification("storage_gb", "128", "GB"));
        pixel9.Activate();

        var iphone15 = new Product(IPhone15Id, "iPhone 15", AppleBrandId, SmartphonesCategoryId,
            "Apple's smartphone with a 48MP main camera.");
        iphone15.AddSpecification(new Specification("camera_mp", "48", "MP"));
        iphone15.AddSpecification(new Specification("battery_mah", "3349", "mAh"));
        iphone15.AddSpecification(new Specification("storage_gb", "128", "GB"));
        iphone15.Activate();

        var xps13 = new Product(Xps13Id, "XPS 13", DellBrandId, LaptopsCategoryId,
            "Dell's compact ultrabook.");
        xps13.AddSpecification(new Specification("cpu", "Intel Core i7"));
        xps13.AddSpecification(new Specification("ram_gb", "16", "GB"));
        xps13.AddSpecification(new Specification("storage_gb", "512", "GB"));
        xps13.Activate();

        var xiaomi14 = new Product(Xiaomi14Id, "Xiaomi 14", XiaomiBrandId, SmartphonesCategoryId,
            "Xiaomi's flagship smartphone with a Leica-tuned camera system.");
        xiaomi14.AddSpecification(new Specification("camera_mp", "50", "MP"));
        xiaomi14.AddSpecification(new Specification("battery_mah", "4610", "mAh"));
        xiaomi14.AddSpecification(new Specification("storage_gb", "256", "GB"));
        xiaomi14.Activate();

        var onePlus12 = new Product(OnePlus12Id, "OnePlus 12", OnePlusBrandId, SmartphonesCategoryId,
            "OnePlus's flagship smartphone with a large 5400 mAh battery.");
        onePlus12.AddSpecification(new Specification("camera_mp", "50", "MP"));
        onePlus12.AddSpecification(new Specification("battery_mah", "5400", "mAh"));
        onePlus12.AddSpecification(new Specification("storage_gb", "256", "GB"));
        onePlus12.Activate();

        var surfaceLaptop6 = new Product(SurfaceLaptop6Id, "Surface Laptop 6", MicrosoftBrandId, LaptopsCategoryId,
            "Microsoft's thin-and-light laptop with an Intel Core Ultra processor.");
        surfaceLaptop6.AddSpecification(new Specification("cpu", "Intel Core Ultra 7"));
        surfaceLaptop6.AddSpecification(new Specification("ram_gb", "16", "GB"));
        surfaceLaptop6.AddSpecification(new Specification("storage_gb", "512", "GB"));
        surfaceLaptop6.Activate();

        var thinkPadX1Carbon = new Product(ThinkPadX1CarbonId, "ThinkPad X1 Carbon", LenovoBrandId, LaptopsCategoryId,
            "Lenovo's flagship business ultrabook.");
        thinkPadX1Carbon.AddSpecification(new Specification("cpu", "Intel Core i7"));
        thinkPadX1Carbon.AddSpecification(new Specification("ram_gb", "32", "GB"));
        thinkPadX1Carbon.AddSpecification(new Specification("storage_gb", "1024", "GB"));
        thinkPadX1Carbon.Activate();

        var ipadAir = new Product(IPadAirId, "iPad Air", AppleBrandId, TabletsCategoryId,
            "Apple's mid-range tablet with a large display.");
        ipadAir.AddSpecification(new Specification("screen_in", "10.9", "in"));
        ipadAir.AddSpecification(new Specification("storage_gb", "128", "GB"));
        ipadAir.AddSpecification(new Specification("battery_mah", "7606", "mAh"));
        ipadAir.Activate();

        var galaxyTabS9 = new Product(GalaxyTabS9Id, "Galaxy Tab S9", SamsungBrandId, TabletsCategoryId,
            "Samsung's flagship Android tablet with an AMOLED display.");
        galaxyTabS9.AddSpecification(new Specification("screen_in", "11", "in"));
        galaxyTabS9.AddSpecification(new Specification("storage_gb", "256", "GB"));
        galaxyTabS9.AddSpecification(new Specification("battery_mah", "8400", "mAh"));
        galaxyTabS9.Activate();

        var xiaomiPad6 = new Product(XiaomiPad6Id, "Xiaomi Pad 6", XiaomiBrandId, TabletsCategoryId,
            "Xiaomi's value Android tablet with a large battery.");
        xiaomiPad6.AddSpecification(new Specification("screen_in", "11", "in"));
        xiaomiPad6.AddSpecification(new Specification("storage_gb", "128", "GB"));
        xiaomiPad6.AddSpecification(new Specification("battery_mah", "8840", "mAh"));
        xiaomiPad6.Activate();

        var sonyWh1000Xm5 = new Product(SonyWh1000Xm5Id, "Sony WH-1000XM5", SonyBrandId, HeadphonesCategoryId,
            "Sony's flagship noise-cancelling over-ear headphones.");
        sonyWh1000Xm5.AddSpecification(new Specification("battery_hours", "30", "h"));
        sonyWh1000Xm5.AddSpecification(new Specification("noise_cancelling", "Yes"));
        sonyWh1000Xm5.AddSpecification(new Specification("weight_g", "250", "g"));
        sonyWh1000Xm5.Activate();

        var boseQuietComfortUltra = new Product(BoseQuietComfortUltraId, "Bose QuietComfort Ultra", BoseBrandId, HeadphonesCategoryId,
            "Bose's flagship noise-cancelling over-ear headphones.");
        boseQuietComfortUltra.AddSpecification(new Specification("battery_hours", "24", "h"));
        boseQuietComfortUltra.AddSpecification(new Specification("noise_cancelling", "Yes"));
        boseQuietComfortUltra.AddSpecification(new Specification("weight_g", "254", "g"));
        boseQuietComfortUltra.Activate();

        var airPodsMax = new Product(AirPodsMaxId, "AirPods Max", AppleBrandId, HeadphonesCategoryId,
            "Apple's over-ear noise-cancelling headphones.");
        airPodsMax.AddSpecification(new Specification("battery_hours", "20", "h"));
        airPodsMax.AddSpecification(new Specification("noise_cancelling", "Yes"));
        airPodsMax.AddSpecification(new Specification("weight_g", "384", "g"));
        airPodsMax.Activate();

        return
        [
            galaxyS24, pixel9, iphone15, xps13,
            xiaomi14, onePlus12, surfaceLaptop6, thinkPadX1Carbon,
            ipadAir, galaxyTabS9, xiaomiPad6,
            sonyWh1000Xm5, boseQuietComfortUltra, airPodsMax,
        ];
    }
}

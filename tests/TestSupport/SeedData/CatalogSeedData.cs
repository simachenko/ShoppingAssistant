using ProductCatalog.Domain;

namespace TestSupport.SeedData;

/// <summary>
/// A small, fixed dataset spanning two categories, reused by contract/integration tests and by
/// the EndToEnd/quickstart scenarios so the same product ids/names are referenced everywhere
/// (e.g., "compare Galaxy S24 and Pixel 9"). Ids are fixed constants, not random, so scenarios
/// can name a specific product deterministically across test runs.
/// </summary>
public static class CatalogSeedData
{
    public static readonly Guid SmartphonesCategoryId = Guid.Parse("00000000-0000-0000-0001-000000000001");
    public static readonly Guid LaptopsCategoryId = Guid.Parse("00000000-0000-0000-0001-000000000002");

    public static readonly Guid SamsungBrandId = Guid.Parse("00000000-0000-0000-0002-000000000001");
    public static readonly Guid GoogleBrandId = Guid.Parse("00000000-0000-0000-0002-000000000002");
    public static readonly Guid AppleBrandId = Guid.Parse("00000000-0000-0000-0002-000000000003");
    public static readonly Guid DellBrandId = Guid.Parse("00000000-0000-0000-0002-000000000004");

    public static readonly Guid GalaxyS24Id = Guid.Parse("00000000-0000-0000-0003-000000000001");
    public static readonly Guid Pixel9Id = Guid.Parse("00000000-0000-0000-0003-000000000002");
    public static readonly Guid IPhone15Id = Guid.Parse("00000000-0000-0000-0003-000000000003");
    public static readonly Guid Xps13Id = Guid.Parse("00000000-0000-0000-0003-000000000004");

    public static IReadOnlyList<Category> Categories { get; } =
    [
        new(SmartphonesCategoryId, "Smartphones", ["camera_mp", "battery_mah", "storage_gb"]),
        new(LaptopsCategoryId, "Laptops", ["cpu", "ram_gb", "storage_gb"]),
    ];

    public static IReadOnlyList<Brand> Brands { get; } =
    [
        new(SamsungBrandId, "Samsung"),
        new(GoogleBrandId, "Google"),
        new(AppleBrandId, "Apple"),
        new(DellBrandId, "Dell"),
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

        return [galaxyS24, pixel9, iphone15, xps13];
    }
}

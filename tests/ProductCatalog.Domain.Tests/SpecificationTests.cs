using ProductCatalog.Domain;

namespace ProductCatalog.Domain.Tests;

public class SpecificationTests
{
    [Fact]
    public void Constructor_throws_when_key_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Specification("", "50", "MP"));
    }

    [Fact]
    public void Constructor_throws_when_value_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Specification("camera_mp", "", "MP"));
    }

    [Fact]
    public void Unit_is_optional()
    {
        var spec = new Specification("camera_mp", "50");

        Assert.Null(spec.Unit);
    }
}

public class BrandTests
{
    [Fact]
    public void Constructor_throws_when_name_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Brand(Guid.NewGuid(), ""));
    }

    [Fact]
    public void Constructor_throws_when_id_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Brand(Guid.Empty, "Samsung"));
    }

    [Fact]
    public void Rename_updates_the_name()
    {
        var brand = new Brand(Guid.NewGuid(), "Samsung");

        brand.Rename("Samsung Electronics");

        Assert.Equal("Samsung Electronics", brand.Name);
    }
}

public class CategoryTests
{
    [Fact]
    public void Constructor_stores_comparable_attribute_keys_in_order()
    {
        var category = new Category(Guid.NewGuid(), "Smartphones", ["camera_mp", "battery_mah", "price"]);

        Assert.Equal(["camera_mp", "battery_mah", "price"], category.ComparableAttributeKeys);
    }

    [Fact]
    public void Constructor_throws_when_name_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Category(Guid.NewGuid(), "", ["price"]));
    }
}

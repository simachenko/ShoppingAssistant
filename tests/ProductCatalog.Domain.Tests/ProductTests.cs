using ProductCatalog.Domain;

namespace ProductCatalog.Domain.Tests;

public class ProductTests
{
    private static Product CreateProduct(string name = "Galaxy S24", Guid categoryId = default) =>
        new(Guid.NewGuid(), name, Guid.NewGuid(), categoryId == default ? Guid.NewGuid() : categoryId);

    [Fact]
    public void Constructor_throws_when_name_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Product(Guid.NewGuid(), "", Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_throws_when_category_id_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Product(Guid.NewGuid(), "Phone", Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void Constructor_throws_when_product_id_is_empty()
    {
        Assert.Throws<ArgumentException>(() => new Product(Guid.Empty, "Phone", Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Activate_throws_when_no_specifications_exist()
    {
        var product = CreateProduct();

        var ex = Assert.Throws<InvalidOperationException>(product.Activate);
        Assert.Contains("at least one specification", ex.Message);
        Assert.False(product.IsActive);
    }

    [Fact]
    public void Activate_succeeds_once_a_specification_is_added()
    {
        var product = CreateProduct();
        product.AddSpecification(new Specification("camera_mp", "50", "MP"));

        product.Activate();

        Assert.True(product.IsActive);
    }

    [Fact]
    public void AddSpecification_rejects_duplicate_keys()
    {
        var product = CreateProduct();
        product.AddSpecification(new Specification("camera_mp", "50", "MP"));

        Assert.Throws<InvalidOperationException>(
            () => product.AddSpecification(new Specification("camera_mp", "108", "MP")));
    }

    [Fact]
    public void Deactivate_clears_the_active_flag()
    {
        var product = CreateProduct();
        product.AddSpecification(new Specification("camera_mp", "50", "MP"));
        product.Activate();

        product.Deactivate();

        Assert.False(product.IsActive);
    }
}

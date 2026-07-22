namespace ProductCatalog.Domain;

public sealed class Brand
{
    public Guid BrandId { get; private set; }
    public string Name { get; private set; } = null!;

    public Brand(Guid brandId, string name)
    {
        if (brandId == Guid.Empty)
            throw new ArgumentException("BrandId is required.", nameof(brandId));

        BrandId = brandId;
        Rename(name);
    }

    private Brand()
    {
        // EF Core materialization only.
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Brand name is required.", nameof(name));

        Name = name;
    }
}

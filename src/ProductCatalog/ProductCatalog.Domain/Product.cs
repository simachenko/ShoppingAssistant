namespace ProductCatalog.Domain;

/// <summary>Aggregate root for the Product Catalog bounded context.</summary>
public sealed class Product
{
    private readonly List<Specification> _specifications = [];
    private readonly List<string> _searchKeywords = [];

    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid BrandId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public IReadOnlyList<Specification> Specifications => _specifications;
    public IReadOnlyList<string> SearchKeywords => _searchKeywords;

    public Product(
        Guid productId,
        string name,
        Guid brandId,
        Guid categoryId,
        string description = "",
        IEnumerable<string>? searchKeywords = null)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId is required.", nameof(productId));
        if (categoryId == Guid.Empty)
            throw new ArgumentException("CategoryId is required.", nameof(categoryId));

        ProductId = productId;
        BrandId = brandId;
        CategoryId = categoryId;
        Description = description ?? string.Empty;
        Rename(name);

        if (searchKeywords is not null)
        {
            _searchKeywords.AddRange(searchKeywords);
        }
    }

    private Product()
    {
        // EF Core materialization only.
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name is required.", nameof(name));

        Name = name;
    }

    public void AddSpecification(Specification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (_specifications.Any(s => s.Key == specification.Key))
        {
            throw new InvalidOperationException(
                $"A specification with key '{specification.Key}' already exists on this product.");
        }

        _specifications.Add(specification);
    }

    /// <summary>
    /// A product must have at least one specification before it can be searchable/active —
    /// an incomplete draft cannot be surfaced to shoppers.
    /// </summary>
    public void Activate()
    {
        if (_specifications.Count == 0)
        {
            throw new InvalidOperationException(
                "A product must have at least one specification before it can be activated.");
        }

        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
}

namespace ProductCatalog.Domain;

public sealed class Category
{
    private readonly List<string> _comparableAttributeKeys = [];

    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>
    /// The canonical, ordered set of specification keys used whenever products in this
    /// category are compared — guarantees identical comparison criteria/order (FR-006/SC-002).
    /// </summary>
    public IReadOnlyList<string> ComparableAttributeKeys => _comparableAttributeKeys;

    public Category(Guid categoryId, string name, IEnumerable<string> comparableAttributeKeys)
    {
        if (categoryId == Guid.Empty)
            throw new ArgumentException("CategoryId is required.", nameof(categoryId));
        ArgumentNullException.ThrowIfNull(comparableAttributeKeys);

        CategoryId = categoryId;
        Rename(name);
        _comparableAttributeKeys.AddRange(comparableAttributeKeys);
    }

    private Category()
    {
        // EF Core materialization only.
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required.", nameof(name));

        Name = name;
    }
}

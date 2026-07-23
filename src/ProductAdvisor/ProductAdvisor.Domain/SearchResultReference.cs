namespace ProductAdvisor.Domain;

/// <summary>
/// A lightweight reference to a product shown to the user (from a search, recommendation, or
/// comparison) — id + name only, no specs/price/availability, so a follow-up that needs those
/// re-fetches them fresh rather than trusting a possibly-stale cached copy (FR-022,
/// research.md §15). Plain class (not a record) — EF Core's owned-JSON constructor binding gets
/// confused by a record's synthesized copy constructor.
/// </summary>
public sealed class SearchResultReference : IEquatable<SearchResultReference>
{
    public Guid ProductId { get; }
    public string Name { get; }

    public SearchResultReference(Guid productId, string name)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId is required.", nameof(productId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        ProductId = productId;
        Name = name;
    }

    public bool Equals(SearchResultReference? other) =>
        other is not null && ProductId == other.ProductId && Name == other.Name;

    public override bool Equals(object? obj) => Equals(obj as SearchResultReference);

    public override int GetHashCode() => HashCode.Combine(ProductId, Name);
}

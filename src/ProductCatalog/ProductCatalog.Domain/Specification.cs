namespace ProductCatalog.Domain;

/// <summary>
/// A single category-defined product attribute (e.g. "camera_mp" = "50" MP).
/// Deliberately a plain class (not a record) — EF Core's owned-JSON constructor binding
/// gets confused by a record's synthesized copy constructor.
/// </summary>
public sealed class Specification : IEquatable<Specification>
{
    public string Key { get; }
    public string Value { get; }
    public string? Unit { get; }

    public Specification(string key, string value, string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Specification key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Specification value is required.", nameof(value));

        Key = key;
        Value = value;
        Unit = unit;
    }

    public bool Equals(Specification? other) =>
        other is not null && Key == other.Key && Value == other.Value && Unit == other.Unit;

    public override bool Equals(object? obj) => Equals(obj as Specification);

    public override int GetHashCode() => HashCode.Combine(Key, Value, Unit);
}

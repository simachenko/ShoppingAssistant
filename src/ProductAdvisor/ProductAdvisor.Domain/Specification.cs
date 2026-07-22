namespace ProductAdvisor.Domain;

/// <summary>Advisor's own read-only copy of a product attribute, as returned by Catalog.</summary>
public sealed record Specification(string Key, string Value, string? Unit = null);

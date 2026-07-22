namespace ProductAdvisor.Domain;

/// <summary>
/// Not persisted — assembled per request from Catalog + Pricing tool-call responses. Never a
/// duplicate/shadow copy of another service's data; it only lives for the duration of a turn.
/// </summary>
public sealed record ProductCandidate
{
    public required Guid ProductId { get; init; }
    public required string Name { get; init; }
    public string? BrandName { get; init; }
    public string? CategoryName { get; init; }
    public IReadOnlyList<Specification> Specifications { get; init; } = [];

    public Money? Price { get; init; }
    public StockStatus? Availability { get; init; }

    /// <summary>Drives "cannot be verified" messaging (FR-005) instead of guessing.</summary>
    public bool PriceVerified { get; init; }
    public bool AvailabilityVerified { get; init; }
}

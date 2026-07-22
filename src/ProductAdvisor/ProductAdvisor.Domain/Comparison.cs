namespace ProductAdvisor.Domain;

/// <summary>The typed result of a <c>compare_products</c> tool call (see ComparisonEngine).</summary>
public sealed record Comparison
{
    public required Guid ComparisonId { get; init; }

    /// <summary>The shared, ordered criteria list — identical for every row (FR-006/SC-002).</summary>
    public required IReadOnlyList<string> Criteria { get; init; }

    public required IReadOnlyList<ComparisonRow> Rows { get; init; }
}

public sealed record ComparisonRow
{
    public required ProductCandidate Candidate { get; init; }

    /// <summary>Null value means that criterion could not be verified for this product (FR-005).</summary>
    public required IReadOnlyDictionary<string, string?> ValuesByCriterion { get; init; }

    /// <summary>Deterministic composite rating — computed identically for every row in the set.</summary>
    public required decimal Rating { get; init; }

    /// <summary>Per-criterion computed difference from the best value present in the set.</summary>
    public required IReadOnlyDictionary<string, string> DeltasVsBest { get; init; }
}

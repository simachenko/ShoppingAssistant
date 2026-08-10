namespace ProductAdvisor.Domain;

/// <summary>The typed result of a <c>get_recommendations</c> tool call (see ScoringPolicy).</summary>
public sealed record Recommendation
{
    public required Guid RecommendationId { get; init; }
    public required IReadOnlyList<RecommendedItem> Items { get; init; }

    /// <summary>
    /// Set instead of <see cref="Items"/> being non-empty-but-wrong when nothing fully matches
    /// the user's hard constraints (FR-010) — mutually exclusive with a non-empty <see cref="Items"/>.
    /// </summary>
    public string? UnmetConstraintExplanation { get; init; }

    /// <summary>
    /// Populated only alongside <see cref="UnmetConstraintExplanation"/> — i.e. only when
    /// <see cref="Items"/> is empty (spec.md FR-082, Assumptions) — never alongside a non-empty
    /// <see cref="Items"/>. A confirmed hard-constraint violator is never silently dropped when
    /// nothing else qualifies; it is surfaced here instead, visibly distinct from a qualifying
    /// match.
    /// </summary>
    public IReadOnlyList<NearestAlternative> NearestAlternatives { get; init; } = [];
}

/// <summary>
/// A product confirmed to violate at least one hard constraint (spec.md FR-080/FR-081) — never
/// eligible for <see cref="Recommendation.Items"/>.
/// </summary>
public sealed record NearestAlternative
{
    public required ProductCandidate Candidate { get; init; }

    /// <summary>At least one entry, one per violated hard constraint (FR-082).</summary>
    public required IReadOnlyList<string> ViolatedConstraints { get; init; }
}

public sealed record RecommendedItem
{
    public required ProductCandidate Candidate { get; init; }

    /// <summary>Which parts of the UserRequirement this product satisfies (FR-008) — deterministic.</summary>
    public required IReadOnlyList<string> MatchedRequirements { get; init; }

    /// <summary>At least one required (FR-009) — deterministically derived, never LLM-authored.</summary>
    public required IReadOnlyList<string> TradeOffs { get; init; }

    /// <summary>Deterministic ranking score — never shown to the user as a fabricated "fact".</summary>
    public required decimal Score { get; init; }
}

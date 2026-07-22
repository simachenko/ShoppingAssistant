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

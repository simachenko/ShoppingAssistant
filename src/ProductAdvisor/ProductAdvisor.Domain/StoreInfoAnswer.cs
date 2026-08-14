namespace ProductAdvisor.Domain;

/// <summary>
/// One retrieved fragment that survived relevance filtering, carrying everything needed both to
/// ground an answer and to cite it (data-model.md). Request-scoped — never persisted.
/// </summary>
public sealed record StoreInfoMatch
{
    public required Guid ChunkId { get; init; }
    public required Guid DocumentId { get; init; }

    /// <summary>Denormalized at query time so building a citation needs no second round trip.</summary>
    public required string DocumentTitle { get; init; }

    public required DocumentType DocumentType { get; init; }
    public required string Language { get; init; }

    /// <summary>The exact chunk text — the sole source <c>AllowedClaims</c> is derived from (FR-007).</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The fused (RRF) relevance score used for the threshold cutoff (FR-011). Deliberately never
    /// exposed to the shopper or to narration — it is a ranking signal, not a fact about the
    /// document that could be restated as a claim.
    /// </summary>
    public required double Score { get; init; }
}

/// <summary>
/// The store-knowledge retrieval result for one turn (data-model.md). An empty
/// <see cref="Matches"/> is the single, unambiguous representation of "nothing relevant enough was
/// found" (FR-009) — deliberately not a separate boolean that could disagree with the list.
/// </summary>
public sealed record StoreInfoAnswer
{
    public IReadOnlyList<StoreInfoMatch> Matches { get; init; } = [];

    public static StoreInfoAnswer Empty { get; } = new();

    /// <summary>True when this turn has grounded evidence to answer from at all (FR-009).</summary>
    public bool HasEvidence => Matches.Count > 0;
}

/// <summary>
/// The link between a claim in an answer and the document that supports it (FR-008,
/// data-model.md) — one per distinct contributing document, carried verbatim from the Evidence
/// Envelope through to the wire contract so it can never be re-derived (or invented) by narration.
/// </summary>
public sealed record Citation
{
    public required Guid DocumentId { get; init; }
    public required string DocumentTitle { get; init; }
    public required Guid ChunkId { get; init; }
}

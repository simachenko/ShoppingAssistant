namespace ProductAdvisor.Infrastructure.Rag;

/// <summary>
/// Deployment configuration for store-knowledge retrieval (research.md §4/§8). Every value here
/// is a tuning/deployment concern the specification deliberately leaves configurable rather than
/// fixing a number for (spec.md 002 Assumptions) — what the spec fixes is that the threshold
/// exists and what happens when nothing clears it (FR-011), not its value.
/// </summary>
public sealed class StoreInfoOptions
{
    public const string SectionName = "StoreInfo";

    /// <summary>
    /// The single store this deployment serves (spec.md Clarifications, Session 2026-08-10). Not
    /// a per-user or per-session value; see <see cref="ProductAdvisor.Application.IStoreContext"/>.
    /// </summary>
    public string StoreId { get; set; } = "default";

    /// <summary>
    /// Minimum fused (RRF) score a chunk must reach to be treated as evidence (FR-011). A chunk
    /// below it is dropped inside retrieval, so "nothing matched" and "nothing was relevant
    /// enough" are indistinguishable to every caller — which is exactly what FR-009's honest
    /// "couldn't find it" answer needs them to be.
    /// </summary>
    public double RelevanceThreshold { get; set; } = 0.02;

    /// <summary>How many candidates each hybrid-search leg contributes before fusion (research.md §8).</summary>
    public int CandidatesPerLeg { get; set; } = 20;

    /// <summary>How many fused matches may become evidence for one answer.</summary>
    public int MaxMatches { get; set; } = 6;

    /// <summary>
    /// Reciprocal Rank Fusion's rank-smoothing constant. 60 is the value from the original RRF
    /// paper and the de-facto default (research.md §8).
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>Additive score bonus for a chunk whose language matches the turn's (FR-021).</summary>
    public double LanguageMatchBoost { get; set; } = 0.01;

    /// <summary>Additive score bonus for a chunk whose document type matches the question's (FR-022).</summary>
    public double DocumentTypeMatchBoost { get; set; } = 0.005;
}

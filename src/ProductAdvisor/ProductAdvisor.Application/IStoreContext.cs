namespace ProductAdvisor.Application;

/// <summary>
/// Resolves which store the current request belongs to — the value behind every retrieval's
/// mandatory store filter (spec.md 002 FR-020).
/// </summary>
/// <remarks>
/// This deployment serves a single configured store (spec.md Clarifications, Session 2026-08-10),
/// so the shipped implementation reads one configured value. It exists as a seam rather than an
/// inlined configuration read so that making the system genuinely multi-store later changes one
/// implementation instead of every query site (research.md §4) — the mandatory filter itself is
/// already applied exactly as a multi-store deployment would apply it.
/// </remarks>
public interface IStoreContext
{
    string CurrentStoreId { get; }
}

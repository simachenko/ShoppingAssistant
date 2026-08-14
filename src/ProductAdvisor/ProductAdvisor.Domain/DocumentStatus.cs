namespace ProductAdvisor.Domain;

/// <summary>
/// A <see cref="StoreDocument"/>'s lifecycle status (spec.md 002 FR-014, data-model.md). Only
/// <see cref="Active"/> documents are ever retrievable as an answer source — a
/// <see cref="Superseded"/> document is retained for traceability but excluded from every
/// retrieval query, which is how a same-topic version conflict is resolved deterministically
/// (FR-012) rather than by narration-time judgment.
/// </summary>
public enum DocumentStatus
{
    Active,
    Superseded,
}

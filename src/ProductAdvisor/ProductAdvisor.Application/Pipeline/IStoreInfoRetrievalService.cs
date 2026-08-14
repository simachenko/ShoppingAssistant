using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The `store_info` route's terminal retrieval call (spec.md 002 FR-003/FR-019), invoked directly
/// by the orchestrator from the already-classified query — never offered to the language model as
/// a free tool choice, exactly like <see cref="IRecommendationService"/> (research.md §2).
/// Implemented in Infrastructure, which owns the hybrid-search query and the embedding call; the
/// orchestrator only ever sees this abstraction.
/// </summary>
public interface IStoreInfoRetrievalService
{
    /// <summary>
    /// Retrieves the store-document fragments relevant enough to ground an answer to
    /// <paramref name="query"/>, or <see cref="StoreInfoAnswer.Empty"/> when nothing clears the
    /// configured relevance threshold (FR-011) — "nothing matched" and "nothing survived the
    /// cutoff" are deliberately the same case to every caller (FR-009).
    /// </summary>
    /// <param name="query">The shopper's message text, as already normalized by input validation.</param>
    /// <param name="language">
    /// The language extraction identified for this turn, used as a ranking preference — never a
    /// filter that could withhold an otherwise-available answer (FR-021/FR-023).
    /// </param>
    /// <remarks>
    /// The store is deliberately not a parameter: it is resolved inside the implementation via
    /// <see cref="IStoreContext"/> so no caller can widen, override, or forget the mandatory store
    /// filter (FR-020, research.md §4).
    /// </remarks>
    Task<StoreInfoAnswer> RetrieveAsync(string query, string language, CancellationToken cancellationToken);
}

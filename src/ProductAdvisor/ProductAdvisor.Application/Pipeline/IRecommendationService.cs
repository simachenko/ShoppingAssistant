using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The `recommend` route's terminal compute call (spec.md FR-066), invoked directly by the
/// orchestrator from the already-merged <see cref="UserRequirement"/> — never from arguments the
/// language model reconstructs itself. Implemented in Infrastructure (which owns the actual
/// `get_recommendations` tool handler); the orchestrator only ever sees this abstraction.
/// </summary>
public interface IRecommendationService
{
    Task<Recommendation> GetRecommendationsAsync(UserRequirement requirement, CancellationToken cancellationToken);
}

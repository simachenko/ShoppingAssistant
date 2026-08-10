using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Tools;

/// <summary>
/// Adapts <see cref="ComputeTools"/>'s deterministic recommendation handler to the
/// <see cref="IRecommendationService"/> port the orchestrator calls directly for the `recommend`
/// route (spec.md FR-066) — same handler, same computation, no separate implementation.
/// </summary>
public sealed class RecommendationService(ComputeTools computeTools) : IRecommendationService
{
    public Task<Recommendation> GetRecommendationsAsync(
        UserRequirement requirement, CancellationToken cancellationToken) =>
        computeTools.GetRecommendationsFromRequirementAsync(requirement, cancellationToken);
}

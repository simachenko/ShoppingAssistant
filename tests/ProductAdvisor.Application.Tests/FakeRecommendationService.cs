using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Stands in for the real, deterministic `recommend`-route compute call (ScoringPolicy behind
/// it) so orchestrator tests can assert the orchestrator relays a recommendation without ever
/// computing one itself (research.md §1) — mirrors the pre-existing FakeChatClient/
/// FakeToolCatalog pattern for this test project.
/// </summary>
public sealed class FakeRecommendationService(Recommendation recommendation) : IRecommendationService
{
    public UserRequirement? LastRequirement { get; private set; }
    public int CallCount { get; private set; }

    public Task<Recommendation> GetRecommendationsAsync(UserRequirement requirement, CancellationToken cancellationToken)
    {
        LastRequirement = requirement;
        CallCount++;
        return Task.FromResult(recommendation);
    }
}

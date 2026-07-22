using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>
/// Captures the last structured, deterministic tool result produced during a conversation
/// turn, so the conversation orchestration loop can return it to the API layer verbatim
/// alongside the LLM's narration — without the orchestrator ever computing it itself
/// (research.md §1). Registered Scoped: one fresh instance per HTTP request/turn.
/// </summary>
public interface IToolResultCapture
{
    Recommendation? Recommendation { get; }

    /// <summary>The exact requirement the <c>get_recommendations</c> tool was called with.</summary>
    UserRequirement? RequirementUsed { get; }

    Comparison? Comparison { get; }

    void SetRecommendation(Recommendation recommendation, UserRequirement requirementUsed);

    void SetComparison(Comparison comparison);
}

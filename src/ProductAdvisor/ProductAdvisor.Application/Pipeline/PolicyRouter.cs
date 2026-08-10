using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The route a turn takes once its intent has been classified and merged into
/// <see cref="UserRequirement"/> (spec.md FR-041). Never a free choice offered to the language
/// model — a deterministic function of state, evaluated in code.
/// </summary>
public enum Route
{
    Recommend,
    Compare,
    Checkout,
    ProductFact,
    Smalltalk,
    Unsupported,
    Clarify,
}

/// <summary>
/// Deterministically decides, from merged session state, which processing route a turn takes
/// (spec.md FR-041, research.md §20). Two turns sharing identical merged state and identical
/// extraction results always select the identical route (SC-026) — this is a pure function,
/// never an LLM decision.
/// </summary>
public static class PolicyRouter
{
    /// <summary>
    /// Below this, a schema-valid extraction result is still treated as insufficient to act on
    /// (spec.md FR-053) — the same honesty posture as missing information, never license to
    /// guess. The exact value is a tuning detail (spec.md Assumptions), not fixed by the cycle
    /// itself.
    /// </summary>
    public const double ConfidenceThreshold = 0.5;

    public static Route SelectRoute(UserRequirement requirement, StructuredIntent intent)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(intent);

        if (intent.Confidence < ConfidenceThreshold)
        {
            return Route.Clarify;
        }

        return intent.Intent switch
        {
            Intent.Smalltalk => Route.Smalltalk,
            Intent.Unsupported => Route.Unsupported,
            Intent.Recommend => requirement.HasEssentialInformation ? Route.Recommend : Route.Clarify,
            Intent.Compare => intent.ProductReferences.Count >= 2 ? Route.Compare : Route.Clarify,
            Intent.Checkout => intent.ProductReferences.Count >= 1 ? Route.Checkout : Route.Clarify,
            Intent.ProductFact => intent.ProductReferences.Count >= 1 ? Route.ProductFact : Route.Clarify,
            _ => Route.Clarify,
        };
    }
}

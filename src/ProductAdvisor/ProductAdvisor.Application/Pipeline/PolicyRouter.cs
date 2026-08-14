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

    /// <summary>
    /// A store-policy question, answered from retrieved store documents (spec.md 002 FR-002).
    /// Like <see cref="Recommend"/>, its terminal call is invoked deterministically by the
    /// orchestrator rather than offered to the language model as a free tool choice.
    /// </summary>
    StoreInfo,
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
            // Unconditional, unlike the product routes below: a store-policy question is
            // self-contained — it names no product that must first resolve, so there is no
            // precondition whose absence would make routing it premature (spec.md 002 FR-024,
            // research.md §3). Insufficient *evidence* is handled later, by retrieval returning
            // no matches (FR-009), never by refusing to route here.
            Intent.StoreInfo => Route.StoreInfo,
            Intent.Recommend => requirement.HasEssentialInformation ? Route.Recommend : Route.Clarify,
            Intent.Compare => intent.ProductReferences.Count >= 2 ? Route.Compare : Route.Clarify,
            Intent.Checkout => intent.ProductReferences.Count >= 1 ? Route.Checkout : Route.Clarify,
            Intent.ProductFact => intent.ProductReferences.Count >= 1 ? Route.ProductFact : Route.Clarify,
            _ => Route.Clarify,
        };
    }
}

using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>
/// The discriminated <c>TurnResult</c> shape (spec.md FR-060–FR-065, data-model.md `TurnResult`)
/// every completed turn resolves to — exactly one of seven mutually exclusive types, assigned by
/// policy routing plus that route's validated tool outcome, never inferred from narration text
/// and never a value the orchestrator computed itself.
/// </summary>
public sealed record AdvisorTurnResult
{
    public required string Type { get; init; }
    public string? Message { get; init; }
    public string? Question { get; init; }
    public Recommendation? Recommendation { get; init; }
    public Comparison? Comparison { get; init; }
    public CheckoutLink? CheckoutLink { get; init; }

    /// <summary>
    /// Only set for <c>type == "error"</c> (FR-065): <c>true</c> marks a temporary/retryable
    /// condition, <c>false</c> marks a request that cannot be fulfilled at all regardless of
    /// retry.
    /// </summary>
    public bool? Degraded { get; init; }

    public static AdvisorTurnResult ForClarification(string question) =>
        new() { Type = "clarification", Question = question };

    public static AdvisorTurnResult ForRecommendation(string message, Recommendation recommendation) =>
        new() { Type = "recommendation", Message = message, Recommendation = recommendation };

    public static AdvisorTurnResult ForComparison(string message, Comparison comparison) =>
        new() { Type = "comparison", Message = message, Comparison = comparison };

    public static AdvisorTurnResult ForCheckoutLink(string message, CheckoutLink checkoutLink) =>
        new() { Type = "checkoutLink", Message = message, CheckoutLink = checkoutLink };

    /// <summary>
    /// A plain conversational reply with no product data attached (`smalltalk`-intent turns,
    /// spec.md FR-060/FR-063) — a first-class result type, not a `clarification` in disguise.
    /// </summary>
    public static AdvisorTurnResult ForAnswer(string message) =>
        new() { Type = "answer", Message = message };

    /// <summary>
    /// A recognized but out-of-scope request (`unsupported`-intent turns, spec.md FR-064) —
    /// never remapped to `clarification` (which implies more information would help) or silently
    /// dropped.
    /// </summary>
    public static AdvisorTurnResult ForUnsupported(string message) =>
        new() { Type = "unsupported", Message = message };

    /// <summary>
    /// Reserved for when tool-result validation fails (FR-043) or no other type-specific result
    /// can be honestly produced this turn (FR-065) — never a generic fallback for "something went
    /// wrong", and never used just because a partial result exists (a partial outage that still
    /// yields a type-specific result keeps that type with unverified fields, per FR-005).
    /// </summary>
    public static AdvisorTurnResult ForError(string message, bool degraded) =>
        new() { Type = "error", Message = message, Degraded = degraded };
}

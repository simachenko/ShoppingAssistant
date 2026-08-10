using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>
/// What the orchestrator produced for one conversation turn — always either a clarification
/// question or a tool-produced result (recommendation or comparison), never a value the
/// orchestrator computed itself.
/// </summary>
public sealed record AdvisorTurnResult
{
    public required string Type { get; init; }
    public string? Message { get; init; }
    public string? Question { get; init; }
    public Recommendation? Recommendation { get; init; }
    public Comparison? Comparison { get; init; }
    public CheckoutLink? CheckoutLink { get; init; }

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
    /// Phase 10 (spec.md FR-060–FR-065) will fold this into the full seven-type discriminated
    /// `TurnResult` contract; this minimal variant only unblocks correct routing for now.
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
}

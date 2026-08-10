namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The turn-level hard limits from spec.md FR-071–FR-079 (data-model.md `TurnResourceBudget`).
/// Deployment configuration — the numbers may differ per environment, but the existence of each
/// limit and its fail-safe outcome are fixed by spec.md, not these defaults.
/// </summary>
public sealed record TurnResourceBudgetOptions
{
    public const string SectionName = "TurnResourceBudget";

    /// <summary>
    /// Approximated by the shared chat client's own <c>MaximumIterationsPerRequest</c>
    /// (<c>AdvisorAiExtensions</c>) rather than a separate counter here — see
    /// <see cref="TurnResourceBudgetGuard"/>.
    /// </summary>
    public int MaxToolCallsPerTurn { get; init; } = 6;

    /// <summary>Mirrors the shared chat client's <c>MaximumConsecutiveErrorsPerRequest</c>.</summary>
    public int MaxConsecutiveToolErrors { get; init; } = 2;

    public TimeSpan OverallTurnTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

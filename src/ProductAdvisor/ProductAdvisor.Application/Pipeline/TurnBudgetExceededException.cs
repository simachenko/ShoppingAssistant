namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Signals that a <see cref="TurnResourceBudgetOptions"/> limit was reached — caught exactly
/// once, at the top of a turn, and translated into <c>AdvisorTurnResult.ForError</c> (spec.md
/// FR-065/FR-071–FR-079). Never allowed to reach the client as an unhandled exception.
/// </summary>
public sealed class TurnBudgetExceededException(string message, bool degraded) : Exception(message)
{
    /// <summary>See <c>AdvisorTurnResult.Degraded</c>: true for every budget limit here — each
    /// one is a resource-exhaustion condition a retry may not hit again, never a request that is
    /// inherently impossible to fulfill.</summary>
    public bool Degraded { get; } = degraded;
}

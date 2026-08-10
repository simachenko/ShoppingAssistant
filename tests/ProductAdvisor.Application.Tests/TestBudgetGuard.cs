using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Application.Tests;

/// <summary>Shared, generously-timed guard for tests that aren't exercising the budget itself.</summary>
public static class TestBudgetGuard
{
    public static TurnResourceBudgetGuard Generous { get; } = new(new TurnResourceBudgetOptions());
}

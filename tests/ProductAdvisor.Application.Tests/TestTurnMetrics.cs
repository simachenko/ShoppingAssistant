using ProductAdvisor.Application.Pipeline;

namespace ProductAdvisor.Application.Tests;

/// <summary>Shared metrics instance for tests that aren't asserting on counter values — a test
/// that does (see <c>TurnMetricsTests</c>) creates its own dedicated instance instead.</summary>
public static class TestTurnMetrics
{
    public static TurnMetrics Instance { get; } = new();
}

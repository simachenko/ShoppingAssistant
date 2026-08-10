using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ProductAdvisor.Application.Tests;

/// <summary>Shared no-op logger for tests that aren't asserting on log output.</summary>
public static class TestLogger
{
    public static ILogger<ConversationOrchestrator> Instance { get; } = NullLogger<ConversationOrchestrator>.Instance;
}

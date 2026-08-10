namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Admission-control/resource-protection limits (spec.md FR-104–FR-113, data-model.md
/// `RequestGuardrails`) — enforced before or independent of a single turn's own processing,
/// distinct from <see cref="TurnResourceBudgetOptions"/> (which governs an already-admitted
/// turn's own LLM/tool usage). Values are deployment configuration; the existence of each limit
/// and its fail-safe (a controlled rejection, zero LLM/tool invocation, FR-113) are fixed by
/// spec.md, not these defaults.
/// </summary>
public sealed record RequestGuardrailOptions
{
    public const string SectionName = "RequestGuardrails";

    public int MaxMessageLength { get; init; } = 2000;

    /// <summary>FR-105: enforced at the transport layer (Kestrel), before the body is parsed —
    /// see each service's own <c>Program.cs</c>, not this project (which has no ASP.NET Core
    /// hosting dependency to configure Kestrel with).</summary>
    public long MaxRequestBodyBytes { get; init; } = 64 * 1024;

    public int MaxListEntries { get; init; } = 20;

    public int MaxListEntryLength { get; init; } = 200;

    /// <summary>FR-112: bounds what's included in a prompt from <c>ConversationSession.Messages</c>
    /// — never what's persisted (the full transcript stays available for the conversation view,
    /// FR-023).</summary>
    public int MaxActiveContextMessages { get; init; } = 20;
}

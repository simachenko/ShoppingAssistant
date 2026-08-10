namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The closed set of fields a turn's logs may contain (spec.md FR-133) — deliberately a fixed
/// shape with no "extra data"/free-form bag, so a caller cannot accidentally attach a denied
/// field (FR-134: the full raw message, the full assembled prompt, PII-bearing tool
/// data/results, credential/API-key/connection-string values, or the full raw LLM response) just
/// by having it in scope. Every field here must already be independently derivable without
/// capturing the prompt/response content it was computed from (FR-135) — e.g. a prompt's
/// character count is fine to compute from the prompt, but the prompt text itself has nowhere to
/// go in this shape.
/// </summary>
public sealed record TurnLogFields
{
    public required string CorrelationId { get; init; }

    /// <summary>Never the raw stable identifier — always <see cref="PseudonymousIdentifier.Hash"/>'s
    /// output (FR-133/FR-137).</summary>
    public string? PseudonymousUserId { get; init; }

    public string? PromptVersion { get; init; }
    public string? ModelIdentifier { get; init; }
    public string? Intent { get; init; }
    public string? ToolName { get; init; }

    /// <summary>A coarse allow/deny decision label (e.g. "route: allowed", "rate-limit: denied")
    /// — never the content that produced the decision (spec.md Assumptions).</summary>
    public string? Decision { get; init; }

    public TimeSpan? Latency { get; init; }
    public long? TokenUsage { get; init; }
    public string? ValidationStatus { get; init; }

    /// <summary>A coarse category, never a raw exception message or stack trace (which could
    /// itself embed a denied field, spec.md Assumptions).</summary>
    public string? ErrorCategory { get; init; }
}

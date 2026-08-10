namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The result of screening one raw user message for potential PII (spec.md FR-116,
/// data-model.md `PiiScreeningResult`) — produced before the structured-intent-extraction call,
/// so a flagged message never reaches the LLM provider unredacted and unblocked.
/// </summary>
public sealed record PiiScreeningResult
{
    public required bool Flagged { get; init; }

    /// <summary>Only meaningful when <see cref="Flagged"/> is true.</summary>
    public string? Action { get; init; }

    /// <summary>Present only when <see cref="Action"/> is <c>"Redacted"</c> — this, never the
    /// original raw text, is what may proceed to extraction.</summary>
    public string? RedactedText { get; init; }

    public static PiiScreeningResult Clean { get; } = new() { Flagged = false };

    public static PiiScreeningResult Blocked() => new() { Flagged = true, Action = "Blocked" };

    public static PiiScreeningResult Redacted(string redactedText) =>
        new() { Flagged = true, Action = "Redacted", RedactedText = redactedText };
}

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The single, deterministically-assembled package the constrained-narration language-model call
/// receives as its ONLY factual input (spec.md FR-086–FR-092, data-model.md `EvidenceEnvelope`).
/// Never itself returned to the client — <see cref="AdvisorTurnResult"/>/`TurnResult` is the
/// turn's external output, built from the same <see cref="CanonicalData"/> this envelope carries.
/// </summary>
public sealed record EvidenceEnvelope
{
    /// <summary>The same closed set as `TurnResult.Type` — known before narration runs.</summary>
    public required string ResultType { get; init; }

    /// <summary>
    /// Route-specific structured shape (<c>Recommendation</c>, <c>Comparison</c>,
    /// <c>CheckoutLink</c>), or <c>null</c> for <c>smalltalk</c>/<c>unsupported</c> — byte-identical
    /// to what the turn's result will also carry; narration never sees a different copy.
    /// </summary>
    public object? CanonicalData { get; init; }

    /// <summary>One entry per verifiable field (FR-092: every <see cref="CanonicalData"/> field
    /// that can be unverified MUST have an entry here).</summary>
    public IReadOnlyDictionary<string, bool> VerificationStatus { get; init; } = new Dictionary<string, bool>();

    /// <summary>Which tool call produced each part of <see cref="CanonicalData"/> (FR-092).</summary>
    public IReadOnlyDictionary<string, string> Provenance { get; init; } = new Dictionary<string, string>();

    /// <summary>Fields present in <see cref="VerificationStatus"/> as <c>false</c>, surfaced as an
    /// explicit list for narration/validation convenience.</summary>
    public IReadOnlyList<string> UnverifiedOrUnavailableFields { get; init; } = [];

    /// <summary>One entry per tool call actually made in this turn's recipe.</summary>
    public IReadOnlyList<ToolExecutionRecord> ToolExecutionStatus { get; init; } = [];

    /// <summary>
    /// Deterministically derived from <see cref="CanonicalData"/> — the exact whitelist output
    /// validation checks narration against (FR-088). Empty for <c>smalltalk</c>/<c>unsupported</c>
    /// (FR-067's zero-tool-call recipes) — not "no envelope," an envelope that correctly permits
    /// zero numeric/factual product claims that turn.
    /// </summary>
    public IReadOnlyList<string> AllowedClaims { get; init; } = [];

    /// <summary>
    /// The one URL a narration is allowed to state verbatim (checkout links only) — kept separate
    /// from <see cref="AllowedClaims"/> since a URL is checked by exact match, not by the
    /// numeric-token normalization <see cref="AllowedClaims"/> uses.
    /// </summary>
    public string? AllowedUrl { get; init; }
}

public sealed record ToolExecutionRecord(string Tool, bool Succeeded);

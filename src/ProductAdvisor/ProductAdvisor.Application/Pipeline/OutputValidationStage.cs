using System.Text.RegularExpressions;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The turn-processing cycle's grounding check (spec.md FR-088–FR-090): every numeric/factual
/// claim in a narration is checked against its <see cref="EvidenceEnvelope"/>'s allowed claims;
/// an ungrounded claim rejects the whole narration in favor of a deterministic, non-LLM fallback
/// built from the Envelope's own canonical data (FR-090) — this stage never alters the turn's
/// structured data or result type (FR-089), only ever `AdvisorTurnResult.Message`. Rejection
/// granularity here is whole-response (spec.md Assumptions: an implementation detail, not fixed
/// by the spec) — simpler and strictly safer than partial-sentence stripping.
/// </summary>
public static partial class OutputValidationStage
{
    public static string Validate(string narration, EvidenceEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(narration))
        {
            return BuildFallback(envelope);
        }

        var allowedClaims = envelope.AllowedClaims.ToHashSet(StringComparer.Ordinal);
        if (NumericClaim.ExtractFrom(narration).Any(number => !allowedClaims.Contains(number)))
        {
            return BuildFallback(envelope);
        }

        foreach (Match url in UrlPattern().Matches(narration))
        {
            if (envelope.AllowedUrl is null || !string.Equals(url.Value, envelope.AllowedUrl, StringComparison.Ordinal))
            {
                return BuildFallback(envelope);
            }
        }

        return narration;
    }

    /// <summary>FR-090: produced by application code from the Envelope's own data — never an
    /// additional language-model call.</summary>
    public static string BuildFallback(EvidenceEnvelope envelope) => envelope.CanonicalData switch
    {
        Recommendation recommendation => BuildRecommendationFallback(recommendation),
        Comparison comparison =>
            $"Compared {comparison.Rows.Count} product(s) across {comparison.Criteria.Count} criteria — see the details below.",
        CheckoutLink checkoutLink => $"Here's your checkout link: {checkoutLink.Url}",
        _ => "Here's what I found.",
    };

    private static string BuildRecommendationFallback(Recommendation recommendation) =>
        recommendation.Items.Count > 0
            ? $"Found {recommendation.Items.Count} matching product(s) — see the details below."
            : recommendation.UnmetConstraintExplanation ?? "No matching products were found.";

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();
}

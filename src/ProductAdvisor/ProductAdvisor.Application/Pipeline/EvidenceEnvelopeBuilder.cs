using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Assembles an <see cref="EvidenceEnvelope"/> from an already tool-result-validated turn outcome
/// — entirely deterministic application code (spec.md FR-091), never influenced by narration
/// text, which doesn't exist yet at assembly time.
/// </summary>
public static class EvidenceEnvelopeBuilder
{
    /// <summary>For <c>smalltalk</c>/<c>unsupported</c> — a correctly-empty envelope that permits
    /// zero numeric/factual claims, not the absence of one (data-model.md "Empty envelopes").</summary>
    public static EvidenceEnvelope Empty(string resultType) => new() { ResultType = resultType };

    public static EvidenceEnvelope ForRecommendation(UserRequirement requirement, Recommendation recommendation)
    {
        var verification = new Dictionary<string, bool>();
        var provenance = new Dictionary<string, string>();
        var claims = new List<string>();

        if (requirement.Budget is not null)
        {
            claims.Add(NumericClaim.Normalize(requirement.Budget.Amount));
        }

        foreach (var item in recommendation.Items)
        {
            TrackCandidate(item.Candidate, verification, provenance, claims);
            claims.Add(NumericClaim.Normalize(item.Score));
            claims.AddRange(NumericClaim.ExtractFrom(item.TradeOffs));
            claims.AddRange(NumericClaim.ExtractFrom(item.MatchedRequirements));
        }

        foreach (var alternative in recommendation.NearestAlternatives)
        {
            TrackCandidate(alternative.Candidate, verification, provenance, claims);
            claims.AddRange(NumericClaim.ExtractFrom(alternative.ViolatedConstraints));
        }

        return new EvidenceEnvelope
        {
            ResultType = "recommendation",
            CanonicalData = recommendation,
            VerificationStatus = verification,
            Provenance = provenance,
            UnverifiedOrUnavailableFields = [.. verification.Where(kv => !kv.Value).Select(kv => kv.Key)],
            ToolExecutionStatus = [new ToolExecutionRecord("get_recommendations", true)],
            AllowedClaims = [.. claims.Distinct(StringComparer.Ordinal)],
        };
    }

    public static EvidenceEnvelope ForComparison(Comparison comparison)
    {
        var verification = new Dictionary<string, bool>();
        var provenance = new Dictionary<string, string>();
        var claims = new List<string>();

        foreach (var row in comparison.Rows)
        {
            TrackCandidate(row.Candidate, verification, provenance, claims);
            claims.Add(NumericClaim.Normalize(row.Rating));

            foreach (var (criterion, value) in row.ValuesByCriterion)
            {
                var field = $"{row.Candidate.ProductId}.{criterion}";
                var verified = value is not null;
                verification[field] = verified;
                provenance[field] = "compare_products";
                if (verified)
                {
                    claims.AddRange(NumericClaim.ExtractFrom(value!));
                }
            }

            claims.AddRange(row.DeltasVsBest.Values.SelectMany(NumericClaim.ExtractFrom));
        }

        return new EvidenceEnvelope
        {
            ResultType = "comparison",
            CanonicalData = comparison,
            VerificationStatus = verification,
            Provenance = provenance,
            UnverifiedOrUnavailableFields = [.. verification.Where(kv => !kv.Value).Select(kv => kv.Key)],
            ToolExecutionStatus = [new ToolExecutionRecord("compare_products", true)],
            AllowedClaims = [.. claims.Distinct(StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// Builds the envelope for a store-policy turn (spec.md 002 FR-007/FR-008, research.md §9).
    /// <c>AllowedClaims</c> is derived from the retrieved chunks' text and nothing else, so output
    /// validation structurally rejects any policy number narration states that no retrieved
    /// fragment contains — the citation requirement and the no-fabrication requirement are
    /// enforced by the same mechanism rather than by asking the model to behave.
    /// </summary>
    public static EvidenceEnvelope ForStoreInfo(StoreInfoAnswer answer, string? answerLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var verification = new Dictionary<string, bool>();
        var provenance = new Dictionary<string, string>();
        var claims = new List<string>();

        foreach (var match in answer.Matches)
        {
            var field = $"{match.DocumentId}.{match.ChunkId}";
            // Retrieved document text is verified by definition: it is quoted store content, not
            // a value looked up from a dependency that might have been unavailable.
            verification[field] = true;
            provenance[field] = "retrieve_store_info";
            claims.AddRange(NumericClaim.ExtractFrom(match.Content));
        }

        // One citation per distinct source document, keeping the earliest (best-ranked) chunk as
        // the representative fragment — a shopper tracing a claim wants the document, and the
        // exact fragment for audit, not one entry per fragment of the same document.
        var citations = answer.Matches
            .GroupBy(m => m.DocumentId)
            .Select(g => g.First())
            .Select(m => new Citation
            {
                DocumentId = m.DocumentId,
                DocumentTitle = m.DocumentTitle,
                ChunkId = m.ChunkId,
            })
            .ToList();

        return new EvidenceEnvelope
        {
            ResultType = "answer",
            CanonicalData = answer,
            VerificationStatus = verification,
            Provenance = provenance,
            UnverifiedOrUnavailableFields = [],
            ToolExecutionStatus = [new ToolExecutionRecord("retrieve_store_info", true)],
            AllowedClaims = [.. claims.Distinct(StringComparer.Ordinal)],
            Citations = citations,
            AnswerLanguage = answerLanguage,
        };
    }

    public static EvidenceEnvelope ForCheckoutLink(CheckoutLink checkoutLink) => new()
    {
        ResultType = "checkoutLink",
        CanonicalData = checkoutLink,
        VerificationStatus = checkoutLink.ProductIds.ToDictionary(id => id.ToString(), _ => true),
        Provenance = checkoutLink.ProductIds.ToDictionary(id => id.ToString(), _ => "generate_checkout_link"),
        ToolExecutionStatus = [new ToolExecutionRecord("generate_checkout_link", true)],
        AllowedUrl = checkoutLink.Url,
    };

    private static void TrackCandidate(
        ProductCandidate candidate,
        Dictionary<string, bool> verification,
        Dictionary<string, string> provenance,
        List<string> claims)
    {
        var idPrefix = candidate.ProductId.ToString();

        verification[$"{idPrefix}.price"] = candidate.PriceVerified;
        provenance[$"{idPrefix}.price"] = "check_price_and_availability";
        if (candidate.PriceVerified && candidate.Price is not null)
        {
            claims.Add(NumericClaim.Normalize(candidate.Price.Amount));
        }

        verification[$"{idPrefix}.availability"] = candidate.AvailabilityVerified;
        provenance[$"{idPrefix}.availability"] = "check_price_and_availability";

        foreach (var spec in candidate.Specifications)
        {
            claims.AddRange(NumericClaim.ExtractFrom(spec.Value));
        }
    }
}

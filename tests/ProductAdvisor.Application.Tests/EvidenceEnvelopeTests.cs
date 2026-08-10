using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 11 (spec.md FR-086/FR-091/FR-092): every value in an Evidence Envelope's canonical data
/// has a tracked verification status and provenance entry; assembly is entirely deterministic
/// application code; the envelope is correctly empty (zero allowed claims) for `smalltalk`/
/// `unsupported`.
/// </summary>
public class EvidenceEnvelopeTests
{
    private static ProductCandidate Candidate(decimal price, bool priceVerified = true, string currency = "UAH") => new()
    {
        ProductId = Guid.NewGuid(),
        Name = "Test Phone",
        Price = new Money(price, currency),
        PriceVerified = priceVerified,
        Availability = StockStatus.InStock,
        AvailabilityVerified = true,
        Specifications = [new Specification("camera_mp", "50", "MP")],
    };

    [Fact]
    public void Every_recommended_items_price_and_availability_have_a_verification_status_and_provenance_entry()
    {
        var candidate = Candidate(14500m);
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [new RecommendedItem { Candidate = candidate, MatchedRequirements = [], TradeOffs = ["ok"], Score = 1m }],
        };

        var envelope = EvidenceEnvelopeBuilder.ForRecommendation(new UserRequirement { Budget = new Money(15000m, "UAH") }, recommendation);

        var idPrefix = candidate.ProductId.ToString();
        Assert.True(envelope.VerificationStatus[$"{idPrefix}.price"]);
        Assert.Equal("check_price_and_availability", envelope.Provenance[$"{idPrefix}.price"]);
        Assert.True(envelope.VerificationStatus[$"{idPrefix}.availability"]);
        Assert.Equal("check_price_and_availability", envelope.Provenance[$"{idPrefix}.availability"]);
    }

    [Fact]
    public void An_unverified_price_is_recorded_as_unverified_and_excluded_from_allowed_claims()
    {
        var candidate = Candidate(14500m, priceVerified: false);
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [new RecommendedItem { Candidate = candidate, MatchedRequirements = [], TradeOffs = ["unverified"], Score = 0m }],
        };

        var envelope = EvidenceEnvelopeBuilder.ForRecommendation(new UserRequirement { Budget = new Money(15000m, "UAH") }, recommendation);

        var idPrefix = candidate.ProductId.ToString();
        Assert.False(envelope.VerificationStatus[$"{idPrefix}.price"]);
        Assert.Contains($"{idPrefix}.price", envelope.UnverifiedOrUnavailableFields);
        Assert.DoesNotContain("14500", envelope.AllowedClaims);
    }

    [Fact]
    public void The_recommendation_envelope_permits_the_verified_price_score_and_spec_value_as_claims()
    {
        var candidate = Candidate(14500m);
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [new RecommendedItem { Candidate = candidate, MatchedRequirements = [], TradeOffs = ["ok"], Score = 3.5m }],
        };

        var envelope = EvidenceEnvelopeBuilder.ForRecommendation(new UserRequirement { Budget = new Money(15000m, "UAH") }, recommendation);

        Assert.Contains("14500", envelope.AllowedClaims);
        Assert.Contains("3.5", envelope.AllowedClaims);
        Assert.Contains("50", envelope.AllowedClaims); // from the camera_mp specification value
        Assert.Contains("15000", envelope.AllowedClaims); // the budget itself is a legitimate claim
    }

    [Theory]
    [InlineData("smalltalk")]
    [InlineData("unsupported")]
    public void Empty_permits_zero_claims_for_non_product_bearing_result_types(string resultType)
    {
        var envelope = EvidenceEnvelopeBuilder.Empty(resultType);

        Assert.Equal(resultType, envelope.ResultType);
        Assert.Null(envelope.CanonicalData);
        Assert.Empty(envelope.AllowedClaims);
        Assert.Empty(envelope.VerificationStatus);
        Assert.Empty(envelope.Provenance);
    }

    [Fact]
    public void Assembly_is_deterministic_across_repeated_calls_for_the_same_result()
    {
        var candidate = Candidate(14500m);
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [new RecommendedItem { Candidate = candidate, MatchedRequirements = [], TradeOffs = ["ok"], Score = 2m }],
        };
        var requirement = new UserRequirement { Budget = new Money(15000m, "UAH") };

        var first = EvidenceEnvelopeBuilder.ForRecommendation(requirement, recommendation);
        var second = EvidenceEnvelopeBuilder.ForRecommendation(requirement, recommendation);

        Assert.Equal(first.AllowedClaims.OrderBy(c => c, StringComparer.Ordinal), second.AllowedClaims.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(first.VerificationStatus, second.VerificationStatus);
    }

    [Fact]
    public void The_checkout_link_envelope_permits_only_its_own_url()
    {
        var checkoutLink = new CheckoutLink { Url = "https://retailer.example/checkout?productIds=abc", ProductIds = [Guid.NewGuid()] };

        var envelope = EvidenceEnvelopeBuilder.ForCheckoutLink(checkoutLink);

        Assert.Equal(checkoutLink.Url, envelope.AllowedUrl);
        Assert.Equal("checkoutLink", envelope.ResultType);
    }

    [Fact]
    public void The_comparison_envelope_tracks_a_verification_entry_per_criterion_per_product()
    {
        var candidateA = Candidate(14500m);
        var candidateB = Candidate(15500m);
        var comparison = new Comparison
        {
            ComparisonId = Guid.NewGuid(),
            Criteria = ["price", "camera_mp"],
            Rows =
            [
                new ComparisonRow
                {
                    Candidate = candidateA,
                    ValuesByCriterion = new Dictionary<string, string?> { ["price"] = "14500", ["camera_mp"] = null },
                    Rating = 4.2m,
                    DeltasVsBest = new Dictionary<string, string> { ["price"] = "cheapest" },
                },
                new ComparisonRow
                {
                    Candidate = candidateB,
                    ValuesByCriterion = new Dictionary<string, string?> { ["price"] = "15500", ["camera_mp"] = "50" },
                    Rating = 3.9m,
                    DeltasVsBest = new Dictionary<string, string> { ["price"] = "+1000 vs cheapest" },
                },
            ],
        };

        var envelope = EvidenceEnvelopeBuilder.ForComparison(comparison);

        Assert.True(envelope.VerificationStatus[$"{candidateA.ProductId}.price"]);
        Assert.False(envelope.VerificationStatus[$"{candidateA.ProductId}.camera_mp"]);
        Assert.Contains("1000", envelope.AllowedClaims);
        Assert.Contains("4.2", envelope.AllowedClaims);
    }
}

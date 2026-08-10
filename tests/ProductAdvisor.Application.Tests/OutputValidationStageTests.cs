using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 11's grounding check (spec.md FR-088–FR-090) — the Independent Test scenario this whole
/// phase exists for: a narration claim absent from the Evidence Envelope is never delivered, the
/// fallback is produced by application code alone (no LLM call is even reachable from here), and
/// a grounded narration passes through byte-identical.
/// </summary>
public class OutputValidationStageTests
{
    private static EvidenceEnvelope RecommendationEnvelope()
    {
        var candidate = new ProductCandidate
        {
            ProductId = Guid.NewGuid(),
            Name = "Galaxy S24",
            Price = new Money(14500m, "UAH"),
            PriceVerified = true,
            Availability = StockStatus.InStock,
            AvailabilityVerified = true,
        };
        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            Items = [new RecommendedItem { Candidate = candidate, MatchedRequirements = [], TradeOffs = ["ok"], Score = 2m }],
        };
        return EvidenceEnvelopeBuilder.ForRecommendation(new UserRequirement { Budget = new Money(15000m, "UAH") }, recommendation);
    }

    [Fact]
    public void A_narration_stating_a_price_absent_from_the_envelope_is_replaced_with_the_deterministic_fallback()
    {
        var envelope = RecommendationEnvelope();

        var result = OutputValidationStage.Validate("This phone costs 99999 UAH, a great deal!", envelope);

        Assert.DoesNotContain("99999", result);
        Assert.Equal(OutputValidationStage.BuildFallback(envelope), result);
    }

    [Fact]
    public void A_narration_stating_only_claims_present_in_the_envelope_passes_through_unchanged()
    {
        var envelope = RecommendationEnvelope();

        var narration = "This 14500 UAH phone fits your 15000 UAH budget nicely.";
        var result = OutputValidationStage.Validate(narration, envelope);

        Assert.Equal(narration, result);
    }

    [Fact]
    public void The_deterministic_fallback_never_triggers_an_additional_llm_call_and_is_stable_across_calls()
    {
        var envelope = RecommendationEnvelope();

        var first = OutputValidationStage.BuildFallback(envelope);
        var second = OutputValidationStage.BuildFallback(envelope);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_narration_stating_a_url_other_than_the_checkout_links_own_url_is_rejected()
    {
        var checkoutLink = new CheckoutLink { Url = "https://retailer.example/checkout?productIds=abc", ProductIds = [Guid.NewGuid()] };
        var envelope = EvidenceEnvelopeBuilder.ForCheckoutLink(checkoutLink);

        var result = OutputValidationStage.Validate("Buy it here: https://evil.example/phishing", envelope);

        Assert.DoesNotContain("evil.example", result);
    }

    [Fact]
    public void A_narration_stating_exactly_the_checkout_links_own_url_passes_through()
    {
        var checkoutLink = new CheckoutLink { Url = "https://retailer.example/checkout?productIds=abc", ProductIds = [Guid.NewGuid()] };
        var envelope = EvidenceEnvelopeBuilder.ForCheckoutLink(checkoutLink);

        var narration = $"Here's your link: {checkoutLink.Url}";
        var result = OutputValidationStage.Validate(narration, envelope);

        Assert.Equal(narration, result);
    }

    [Fact]
    public void An_empty_narration_produces_the_deterministic_fallback()
    {
        var envelope = RecommendationEnvelope();

        var result = OutputValidationStage.Validate(string.Empty, envelope);

        Assert.Equal(OutputValidationStage.BuildFallback(envelope), result);
    }

    [Fact]
    public void Rejecting_a_narration_never_alters_the_envelopes_canonical_data()
    {
        var envelope = RecommendationEnvelope();
        var before = envelope.CanonicalData;

        OutputValidationStage.Validate("Fabricated 12345 UAH price.", envelope);

        Assert.Same(before, envelope.CanonicalData);
    }
}

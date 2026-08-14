using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// The `store_info` route's place in the deterministic turn cycle (spec.md 002 FR-002/FR-003) and
/// the Evidence Envelope it produces (FR-007/FR-008). These assert the structural guarantees —
/// that grounding and citation come from the code path, not from prompt wording.
/// </summary>
public class StoreInfoRoutingTests
{
    private static UserRequirement AnyRequirement => UserRequirement.Empty;

    private static StructuredIntent StoreInfoIntent(double confidence = 0.9) =>
        new() { Intent = Intent.StoreInfo, Confidence = confidence, Language = "en" };

    [Fact]
    public void A_store_info_intent_routes_to_the_store_info_route()
    {
        var route = PolicyRouter.SelectRoute(AnyRequirement, StoreInfoIntent());

        Assert.Equal(Route.StoreInfo, route);
    }

    [Fact]
    public void A_store_info_turn_routes_without_needing_a_product_reference_or_a_budget()
    {
        // Unlike compare/checkout/product_fact, a store-policy question names no product and needs
        // no requirement state — gating it on either would produce spurious clarifications.
        var intent = StoreInfoIntent();
        Assert.Empty(intent.ProductReferences);
        Assert.Null(AnyRequirement.Budget);

        Assert.Equal(Route.StoreInfo, PolicyRouter.SelectRoute(AnyRequirement, intent));
    }

    [Fact]
    public void A_low_confidence_store_info_intent_still_falls_back_to_clarification()
    {
        // The shared confidence gate (FR-053) applies to this route exactly as it does to the
        // others — the new route is not an exemption from the cycle's existing rules.
        var route = PolicyRouter.SelectRoute(AnyRequirement, StoreInfoIntent(confidence: 0.1));

        Assert.Equal(Route.Clarify, route);
    }

    [Fact]
    public void The_store_info_envelope_allows_only_claims_present_in_the_retrieved_fragments()
    {
        var answer = FakeStoreInfoRetrievalService.AnswerWith(
            "Standard delivery within Kyiv takes 1-2 business days.");

        var envelope = EvidenceEnvelopeBuilder.ForStoreInfo(answer);

        // "1" and "2" come from the fragment; a number the fragment never stated must not be
        // allowed, which is what makes FR-007 structural rather than a prompt instruction.
        // (Claims are normalized decimal strings — see the internal NumericClaim.)
        Assert.Contains("1", envelope.AllowedClaims);
        Assert.Contains("2", envelope.AllowedClaims);
        Assert.DoesNotContain("14", envelope.AllowedClaims);
    }

    [Fact]
    public void The_store_info_envelope_carries_one_citation_per_distinct_source_document()
    {
        var deliveryId = Guid.NewGuid();
        var returnsId = Guid.NewGuid();
        var answer = new StoreInfoAnswer
        {
            Matches =
            [
                Match(deliveryId, "Delivery Terms", "Delivery takes 1-2 business days."),
                // A second fragment of the SAME document must not produce a second citation.
                Match(deliveryId, "Delivery Terms", "Free delivery over 2000 UAH."),
                Match(returnsId, "Returns and Exchanges", "You may return within 14 calendar days."),
            ],
        };

        var envelope = EvidenceEnvelopeBuilder.ForStoreInfo(answer);

        Assert.Equal(2, envelope.Citations.Count);
        Assert.Contains(envelope.Citations, c => c.DocumentId == deliveryId && c.DocumentTitle == "Delivery Terms");
        Assert.Contains(envelope.Citations, c => c.DocumentId == returnsId);
    }

    [Fact]
    public void An_empty_retrieval_result_produces_an_envelope_that_permits_no_claims_and_cites_nothing()
    {
        var envelope = EvidenceEnvelopeBuilder.ForStoreInfo(StoreInfoAnswer.Empty);

        Assert.Empty(envelope.AllowedClaims);
        Assert.Empty(envelope.Citations);
        // Not "no envelope" — an envelope that correctly permits zero factual claims, so any
        // narration stating a policy detail is rejected by output validation (FR-009/FR-010).
        Assert.Equal("answer", envelope.ResultType);
    }

    [Fact]
    public void Output_validation_rejects_a_narration_stating_a_number_no_fragment_contains()
    {
        var answer = FakeStoreInfoRetrievalService.AnswerWith("You may return an item within 14 calendar days.");
        var envelope = EvidenceEnvelopeBuilder.ForStoreInfo(answer);

        var validated = OutputValidationStage.Validate("You may return an item within 30 calendar days.", envelope);

        // The fabricated "30" must not survive — the shopper gets the deterministic fallback that
        // points at the cited document instead of an invented policy.
        Assert.DoesNotContain("30", validated, StringComparison.Ordinal);
        Assert.Equal(StoreInfoMessages.SeeCitedDocuments(), validated);
    }

    [Fact]
    public void Output_validation_keeps_a_narration_whose_numbers_all_come_from_the_fragments()
    {
        var answer = FakeStoreInfoRetrievalService.AnswerWith("You may return an item within 14 calendar days.");
        var envelope = EvidenceEnvelopeBuilder.ForStoreInfo(answer);

        const string grounded = "You can return it within 14 days.";
        Assert.Equal(grounded, OutputValidationStage.Validate(grounded, envelope));
    }

    private static StoreInfoMatch Match(Guid documentId, string title, string content) => new()
    {
        ChunkId = Guid.NewGuid(),
        DocumentId = documentId,
        DocumentTitle = title,
        DocumentType = DocumentType.Delivery,
        Language = "en",
        Content = content,
        Score = 0.5,
    };
}

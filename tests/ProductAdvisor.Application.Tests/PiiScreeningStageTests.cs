using ProductAdvisor.Application.Pipeline;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 12 (spec.md FR-114/FR-116): every message is screened for potential PII before it — or
/// anything derived from it — reaches the LLM provider. Equally important is what must NOT be
/// flagged: ordinary shopping messages are full of bare numbers (prices, specification values)
/// that a careless pattern could mistake for a phone number or payment card.
/// </summary>
public class PiiScreeningStageTests
{
    [Fact]
    public void A_message_with_no_pii_is_not_flagged()
    {
        var result = PiiScreeningStage.Screen("I need a smartphone with a good camera and a budget of up to 15000 UAH");

        Assert.False(result.Flagged);
        Assert.Null(result.RedactedText);
    }

    [Fact]
    public void An_email_address_is_redacted_not_blocked()
    {
        var result = PiiScreeningStage.Screen("Contact me at jane.doe@example.com about this order");

        Assert.True(result.Flagged);
        Assert.Equal("Redacted", result.Action);
        Assert.DoesNotContain("jane.doe@example.com", result.RedactedText);
        Assert.Contains("[redacted]", result.RedactedText);
    }

    [Fact]
    public void A_phone_number_with_separators_is_redacted()
    {
        var result = PiiScreeningStage.Screen("Call me at +380 67 123 4567 if you find a good deal");

        Assert.True(result.Flagged);
        Assert.Equal("Redacted", result.Action);
        Assert.DoesNotContain("380 67 123 4567", result.RedactedText);
    }

    [Fact]
    public void A_credit_card_shaped_sequence_is_blocked_outright()
    {
        var result = PiiScreeningStage.Screen("My card number is 4111 1111 1111 1111, please charge it");

        Assert.True(result.Flagged);
        Assert.Equal("Blocked", result.Action);
        Assert.Null(result.RedactedText);
    }

    [Theory]
    [InlineData("I need a smartphone under 15000 UAH")]
    [InlineData("My budget is 30000 UAH for a laptop")]
    [InlineData("Looking for a camera with at least 50 MP")]
    [InlineData("The battery should be around 5000 mAh")]
    [InlineData("I have about 500 dollars to spend")]
    public void Bare_price_and_specification_numbers_are_never_flagged(string message)
    {
        // The critical false-positive guard: this domain's messages are full of bare numbers,
        // none of which have phone-number-style separators or credit-card-length digit runs.
        var result = PiiScreeningStage.Screen(message);

        Assert.False(result.Flagged);
    }

    [Fact]
    public void Every_message_is_screened_regardless_of_apparent_topic()
    {
        // FR-114: PII can appear in a message that looks entirely product-related.
        var result = PiiScreeningStage.Screen("Ship the smartphone to jane.doe@example.com please");

        Assert.True(result.Flagged);
    }
}

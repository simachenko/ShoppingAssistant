using ProductAdvisor.Application.Pipeline;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 12 (spec.md FR-104/FR-107/FR-113): an oversized or control-character-bearing message is
/// rejected outright — before any LLM call — via <see cref="GuardrailRejectionException"/>,
/// never silently truncated or passed through.
/// </summary>
public class InputValidationStageTests
{
    private static readonly RequestGuardrailOptions Options = new() { MaxMessageLength = 50 };

    [Fact]
    public void A_message_within_the_length_limit_passes_through_normalized()
    {
        var result = InputValidationStage.ValidateAndNormalize("I need a smartphone under 15000 UAH", Options);

        Assert.Equal("I need a smartphone under 15000 UAH", result);
    }

    [Fact]
    public void A_message_exceeding_the_configured_max_length_is_rejected()
    {
        var oversized = new string('a', Options.MaxMessageLength + 1);

        var ex = Assert.Throws<GuardrailRejectionException>(() => InputValidationStage.ValidateAndNormalize(oversized, Options));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void A_message_containing_a_disallowed_control_character_is_rejected()
    {
        var withControlChar = "HelloWorld";

        var ex = Assert.Throws<GuardrailRejectionException>(() => InputValidationStage.ValidateAndNormalize(withControlChar, Options));

        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("Line one\nLine two")]
    [InlineData("Tab\there")]
    [InlineData("Carriage\rReturn")]
    public void Ordinary_whitespace_control_characters_are_allowed(string message)
    {
        var result = InputValidationStage.ValidateAndNormalize(message, Options);

        Assert.Equal(message, result);
    }
}

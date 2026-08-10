using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// The structured-intent-extraction stage (spec.md FR-038–FR-039/FR-048–FR-051): a schema-valid
/// result passes through; a schema-invalid result triggers exactly one repair attempt; a second
/// failure falls back to null (the orchestrator's signal to produce a clarification), never a
/// third attempt or use of invalid data.
/// </summary>
public class ExtractionStageTests
{
    [Fact]
    public async Task A_schema_valid_result_passes_through_on_the_first_attempt()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Recommend("smartphones", 15000m, "UAH"));
        var stage = new ExtractionStage(chatClient);

        var result = await stage.ExtractAsync(
            UserRequirement.Empty, "I need a smartphone under 15000 UAH", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(Intent.Recommend, result.Intent);
        Assert.Equal("smartphones", result.RequirementPatch?.Category);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task A_malformed_first_response_triggers_exactly_one_repair_attempt_then_succeeds()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Malformed, ExtractionJson.Recommend("laptops", 30000m, "UAH"));
        var stage = new ExtractionStage(chatClient);

        var result = await stage.ExtractAsync(UserRequirement.Empty, "a laptop under 30000", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("laptops", result.RequirementPatch?.Category);
        Assert.Equal(2, chatClient.CallCount);
    }

    [Fact]
    public async Task Two_consecutive_malformed_responses_produce_null_never_a_third_attempt()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Malformed, ExtractionJson.Malformed);
        var stage = new ExtractionStage(chatClient);

        var result = await stage.ExtractAsync(UserRequirement.Empty, "gibberish", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(2, chatClient.CallCount);
    }

    [Fact]
    public async Task An_intent_value_outside_the_closed_set_is_treated_as_a_schema_failure()
    {
        var chatClient = new FakeChatClient(ExtractionJson.UnrecognizedIntent(), ExtractionJson.UnrecognizedIntent());
        var stage = new ExtractionStage(chatClient);

        var result = await stage.ExtractAsync(UserRequirement.Empty, "do something else entirely", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(2, chatClient.CallCount);
    }

    [Fact]
    public async Task Product_references_and_missing_fields_are_captured()
    {
        var chatClient = new FakeChatClient(ExtractionJson.Compare(["Galaxy S24", "Pixel 9"]));
        var stage = new ExtractionStage(chatClient);

        var result = await stage.ExtractAsync(UserRequirement.Empty, "compare Galaxy S24 and Pixel 9", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(Intent.Compare, result.Intent);
        Assert.Equal(["Galaxy S24", "Pixel 9"], result.ProductReferences);
    }
}

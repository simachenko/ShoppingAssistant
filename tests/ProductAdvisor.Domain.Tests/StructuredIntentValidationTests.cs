using System.Text.Json;
using System.Text.Json.Serialization;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Domain.Tests;

/// <summary>
/// The extraction stage's output shape (spec.md FR-048/FR-049, extended by spec.md 002 FR-002):
/// `Intent` is drawn from a fixed, closed seven-value set — an intent value the system does not
/// recognize is a schema-validation failure (enforced at the C# type level: there is no eighth
/// value to construct), never a new, unrecognized route.
/// </summary>
public class StructuredIntentValidationTests
{
    [Fact]
    public void The_Intent_enum_has_exactly_the_seven_values_the_specification_defines()
    {
        var values = Enum.GetValues<Intent>();

        // The count assertion is the point of this test: it fails loudly if a value is added
        // without a corresponding specification change, which is exactly what happened when
        // `store_info` was introduced (spec.md 002 FR-002).
        Assert.Equal(7, values.Length);
        Assert.Contains(Intent.Recommend, values);
        Assert.Contains(Intent.ProductFact, values);
        Assert.Contains(Intent.Compare, values);
        Assert.Contains(Intent.Checkout, values);
        Assert.Contains(Intent.Smalltalk, values);
        Assert.Contains(Intent.Unsupported, values);
        Assert.Contains(Intent.StoreInfo, values);
    }

    [Fact]
    public void The_store_info_intent_uses_the_literal_wire_value_the_extraction_prompt_names()
    {
        // The extraction stage serializes through a string-enum converter with a camelCase naming
        // policy. Without [JsonStringEnumMemberName] that policy would produce `storeInfo`, which
        // the prompt never tells the model to emit — so the model's `store_info` would fail to
        // bind and the turn would silently fall back to clarification. This asserts the attribute
        // wins over the policy, which is the only reason that failure mode does not exist.
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        Assert.Equal("\"store_info\"", JsonSerializer.Serialize(Intent.StoreInfo, options));
        Assert.Equal(Intent.StoreInfo, JsonSerializer.Deserialize<Intent>("\"store_info\"", options));
    }

    [Fact]
    public void A_RequirementPatch_with_every_field_absent_is_a_valid_no_op_patch()
    {
        var patch = new RequirementPatch();

        Assert.Null(patch.Category);
        Assert.Null(patch.Budget);
        Assert.Null(patch.RequiredFeatures);
        Assert.Null(patch.Preferences);
        Assert.Null(patch.AvailabilityRequirements);
    }

    [Fact]
    public void StructuredIntent_requires_Intent_Confidence_and_Language_at_construction()
    {
        // Compiles only because Intent/Confidence/Language are `required` — this test documents
        // that guarantee; omitting any of them is a compile-time error, not a runtime check.
        var intent = new StructuredIntent { Intent = Intent.Smalltalk, Confidence = 0.9, Language = "en" };

        Assert.Equal(Intent.Smalltalk, intent.Intent);
        Assert.Empty(intent.ProductReferences);
        Assert.Empty(intent.MissingFields);
        Assert.Null(intent.RequirementPatch);
    }
}

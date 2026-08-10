using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Domain.Tests;

/// <summary>
/// The extraction stage's output shape (spec.md FR-048/FR-049): `Intent` is drawn from a fixed,
/// closed six-value set — an intent value the system does not recognize is a schema-validation
/// failure (enforced at the C# type level: there is no seventh value to construct), never a new,
/// unrecognized route.
/// </summary>
public class StructuredIntentValidationTests
{
    [Fact]
    public void The_Intent_enum_has_exactly_the_six_values_the_specification_defines()
    {
        var values = Enum.GetValues<Intent>();

        Assert.Equal(6, values.Length);
        Assert.Contains(Intent.Recommend, values);
        Assert.Contains(Intent.ProductFact, values);
        Assert.Contains(Intent.Compare, values);
        Assert.Contains(Intent.Checkout, values);
        Assert.Contains(Intent.Smalltalk, values);
        Assert.Contains(Intent.Unsupported, values);
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

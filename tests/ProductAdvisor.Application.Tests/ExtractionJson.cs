using System.Text.Json;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Builds extraction-stage-shaped JSON payloads for <see cref="FakeChatClient"/>, matching
/// <c>ExtractionStage</c>'s wire DTO (spec.md FR-048). Kept independent of the DTO's own
/// (private) type so these tests exercise the same case-insensitive deserialization path real
/// model output would.
/// </summary>
public static class ExtractionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web);

    public static string Build(object payload) => JsonSerializer.Serialize(payload, Options);

    public static string Recommend(
        string category,
        decimal budgetAmount,
        string budgetCurrency,
        string[]? requiredFeatures = null,
        string[]? preferences = null,
        double confidence = 0.9,
        string language = "en") =>
        Build(new
        {
            intent = "recommend",
            requirementPatch = new
            {
                category,
                budgetAmount,
                budgetCurrency,
                requiredFeatures = requiredFeatures ?? [],
                preferences = preferences ?? [],
            },
            productReferences = Array.Empty<string>(),
            missingFields = Array.Empty<string>(),
            confidence,
            language,
        });

    public static string RequirementPatchOnly(
        string? category = null,
        decimal? budgetAmount = null,
        string? budgetCurrency = null,
        string[]? requiredFeatures = null,
        double confidence = 0.9,
        string intent = "recommend",
        string[]? missingFields = null,
        string language = "en") =>
        Build(new
        {
            intent,
            requirementPatch = new
            {
                category,
                budgetAmount,
                budgetCurrency,
                requiredFeatures,
            },
            productReferences = Array.Empty<string>(),
            missingFields = missingFields ?? [],
            confidence,
            language,
        });

    public static string Smalltalk(double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "smalltalk",
        productReferences = Array.Empty<string>(),
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    public static string Unsupported(double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "unsupported",
        productReferences = Array.Empty<string>(),
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    public static string ProductFact(string[] productReferences, double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "product_fact",
        productReferences,
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    public static string Compare(string[] productReferences, double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "compare",
        productReferences,
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    public static string Checkout(string[] productReferences, double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "checkout",
        productReferences,
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    /// <summary>A store-policy question (spec.md 002 FR-002) — no product reference by design.</summary>
    public static string StoreInfo(double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "store_info",
        productReferences = Array.Empty<string>(),
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    public static string LowConfidence(string intent = "recommend", double confidence = 0.1, string[]? missingFields = null, string language = "en") => Build(new
    {
        intent,
        missingFields = missingFields ?? Array.Empty<string>(),
        productReferences = Array.Empty<string>(),
        confidence,
        language,
    });

    /// <summary>An out-of-schema `intent` value — must be treated as a schema-validation failure (FR-049).</summary>
    public static string UnrecognizedIntent(double confidence = 0.9, string language = "en") => Build(new
    {
        intent = "delete_everything",
        productReferences = Array.Empty<string>(),
        missingFields = Array.Empty<string>(),
        confidence,
        language,
    });

    public const string Malformed = "{ this is not valid json at all";
}

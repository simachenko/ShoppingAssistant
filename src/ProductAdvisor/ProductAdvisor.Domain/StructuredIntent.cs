using System.Text.Json.Serialization;

namespace ProductAdvisor.Domain;

/// <summary>
/// Closed set of turn intents (spec.md FR-048/FR-049). An extraction result naming anything
/// outside this set is a schema-validation failure, never a new, unrecognized route.
/// <see cref="JsonStringEnumMemberNameAttribute"/> pins the wire value to spec.md's literal
/// `product_fact` rather than the naming-policy-transformed default (`productFact`), so the
/// extraction prompt's instructions and the actual deserialized wire format never disagree.
/// </summary>
public enum Intent
{
    Recommend,

    [JsonStringEnumMemberName("product_fact")]
    ProductFact,

    Compare,
    Checkout,
    Smalltalk,
    Unsupported,
}

/// <summary>
/// The changes one turn implies for <see cref="UserRequirement"/> (spec.md FR-057/FR-058). A
/// null field means "not mentioned this turn — leave it alone"; a non-null field (including an
/// empty list, for the list-typed fields) means "the user stated this — apply it." Never
/// constructed by anything other than a schema-valid extraction result.
/// </summary>
public sealed record RequirementPatch
{
    public string? Category { get; init; }
    public Money? Budget { get; init; }
    public IReadOnlyList<string>? RequiredFeatures { get; init; }
    public IReadOnlyList<string>? Preferences { get; init; }
    public IReadOnlyList<string>? AvailabilityRequirements { get; init; }
    public string? Language { get; init; }
    public string? Currency { get; init; }
    public string? Units { get; init; }

    /// <summary>
    /// How many recommendation items the user explicitly asked for (e.g. "just the best one",
    /// "top 3") — null means "not mentioned this turn," the same carry-forward-until-changed
    /// semantics every other field here already has. A non-positive value is mapped to null
    /// before it ever reaches this type (the extraction stage's DTO-to-domain mapping) — it is
    /// treated the same as "not mentioned" rather than crashing or producing an empty recommendation.
    /// </summary>
    public int? ResultLimit { get; init; }
}

/// <summary>
/// The validated, schema-conformant output of the structured-intent-extraction stage (spec.md
/// FR-048). Request-scoped, superseded every turn, never persisted with any chain-of-thought
/// attached (FR-052) — it only identifies what the user wants, never what the answer is.
/// </summary>
public sealed record StructuredIntent
{
    public required Intent Intent { get; init; }
    public RequirementPatch? RequirementPatch { get; init; }
    public IReadOnlyList<string> ProductReferences { get; init; } = [];
    public IReadOnlyList<string> MissingFields { get; init; } = [];
    public required double Confidence { get; init; }
    public string Language { get; init; } = "en";
}

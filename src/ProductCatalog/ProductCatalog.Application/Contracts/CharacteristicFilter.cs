using System.Text.Json.Serialization;

namespace ProductCatalog.Application.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<CharacteristicFilterOperator>))]
public enum CharacteristicFilterOperator
{
    [JsonStringEnumMemberName("eq")]
    Equals,
    [JsonStringEnumMemberName("gte")]
    GreaterThanOrEqual,
    [JsonStringEnumMemberName("lte")]
    LessThanOrEqual,
    [JsonStringEnumMemberName("between")]
    Between,
}

/// <summary>
/// A single structured characteristic condition (FR-020) — request-only, never persisted.
/// <see cref="ValueTo"/> is required when <see cref="Operator"/> is <see cref="CharacteristicFilterOperator.Between"/>
/// and must be absent otherwise (data-model.md's `CharacteristicFilter`).
/// </summary>
public sealed record CharacteristicFilter(string Key, CharacteristicFilterOperator Operator, string Value, string? ValueTo = null);

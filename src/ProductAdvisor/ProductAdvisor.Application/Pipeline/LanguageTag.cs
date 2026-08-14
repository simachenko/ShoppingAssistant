namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Normalizes language tags so a tag and its region-qualified variants compare equal (spec.md 002
/// FR-030) — <c>uk</c>, <c>uk-UA</c>, and <c>UK</c> are all the same language.
/// </summary>
/// <remarks>
/// Without this, a shopper whose language is reported with a region subtag silently loses the
/// same-language retrieval preference they would have got without it, and the deterministic
/// "couldn't find it" message falls back to its default language for no reason the shopper could
/// understand. Both sides of every comparison go through here.
/// </remarks>
public static class LanguageTag
{
    public const string Default = "en";

    /// <summary>
    /// Reduces a tag to its lowercase primary subtag. Unknown/blank input yields
    /// <see cref="Default"/> rather than throwing — a malformed language is a reason to fall back,
    /// never a reason to fail a shopper's turn.
    /// </summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Default;
        }

        var trimmed = tag.Trim();
        var separator = trimmed.IndexOfAny(['-', '_']);
        var primary = separator > 0 ? trimmed[..separator] : trimmed;

        return primary.Length == 0 ? Default : primary.ToLowerInvariant();
    }

    /// <summary>True when both tags denote the same language, ignoring region and case.</summary>
    public static bool SameLanguage(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}

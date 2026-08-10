using System.Globalization;
using System.Text.RegularExpressions;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Numeric-token extraction/normalization shared by <see cref="EvidenceEnvelopeBuilder"/> (which
/// records each canonical value as a claim) and <see cref="OutputValidationStage"/> (which scans
/// narration text for numbers and checks them against those claims) — both sides normalize
/// through the exact same path so "14500", "14,500", and "14500.00" are always recognized as the
/// same claim regardless of which one produced the digits.
/// </summary>
internal static partial class NumericClaim
{
    public static string Normalize(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static IEnumerable<string> ExtractFrom(string text) =>
        Pattern().Matches(text).Select(m => Normalize(m.Value));

    public static IEnumerable<string> ExtractFrom(IEnumerable<string> texts) => texts.SelectMany(ExtractFrom);

    private static string Normalize(string raw)
    {
        var cleaned = raw.Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Normalize(value)
            : cleaned;
    }

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?")]
    private static partial Regex Pattern();
}

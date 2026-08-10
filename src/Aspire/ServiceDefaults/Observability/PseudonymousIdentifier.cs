using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Turns a stable identifier (a user id, a session id) into a value that may appear in the
/// shared logging/tracing backend without itself being the raw identifier (spec.md FR-133/
/// FR-137, research.md §16). A SHA-256 hash is a one-way function — irreversible to the original
/// value from the logged output alone, which is the only property FR-137 requires; this does not
/// promise the pseudonym stays stable if the pepper below is ever rotated (spec.md Assumptions:
/// pseudonyms under different schemes are never assumed correlatable to each other).
/// </summary>
public static class PseudonymousIdentifier
{
    /// <summary>
    /// Defense-in-depth against rainbow-table lookups on a small/guessable identifier space
    /// (e.g. sequential user ids) — override via the <c>ObservabilityPepper</c> configuration key
    /// in a real deployment; the built-in default is not a secret, only a baseline.
    /// </summary>
    private const string DefaultPepper = "productadvisor-observability-v1";

    public static string Hash(string rawIdentifier, string? pepper = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawIdentifier);

        var bytes = Encoding.UTF8.GetBytes(rawIdentifier + (pepper ?? DefaultPepper));
        var hash = SHA256.HashData(bytes);

        // Truncated for log readability — still well beyond brute-force range for this purpose,
        // and truncation only removes distinguishing power, never adds any reversibility.
        return Convert.ToHexString(hash)[..16];
    }
}

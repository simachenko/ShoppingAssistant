using System.Text.RegularExpressions;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// Screens a raw user message for potential PII before it — or anything derived from it — is
/// included in any call to the LLM provider (spec.md FR-114/FR-116). Every message is treated as
/// capable of containing PII unrelated to the product domain, regardless of what it appears to
/// be about (FR-114) — this runs unconditionally, not only for messages that "look like" they
/// might contain PII. Which specific patterns trigger blocking versus redaction is an
/// implementation decision (spec.md Assumptions, mirroring FR-088's own reject/strip/replace
/// choice); a credit-card-shaped sequence is blocked outright (FR-115: this system must never
/// even solicit a payment-card number, let alone forward one), while an email address or phone
/// number is redacted so the turn can still proceed on the non-PII parts of the message.
/// </summary>
public static partial class PiiScreeningStage
{
    public static PiiScreeningResult Screen(string message)
    {
        if (CreditCardPattern().IsMatch(message))
        {
            return PiiScreeningResult.Blocked();
        }

        var redacted = message;
        var flagged = false;

        if (EmailPattern().IsMatch(redacted))
        {
            flagged = true;
            redacted = EmailPattern().Replace(redacted, "[redacted]");
        }

        if (PhonePattern().IsMatch(redacted))
        {
            flagged = true;
            redacted = PhonePattern().Replace(redacted, "[redacted]");
        }

        return flagged ? PiiScreeningResult.Redacted(redacted) : PiiScreeningResult.Clean;
    }

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex EmailPattern();

    /// <summary>Requires at least two separator-delimited digit groups (an optional country
    /// code, then 2–4 groups) — a bare number with no internal separator, like a price ("15000")
    /// or a specification value ("50"), never matches this.</summary>
    [GeneratedRegex(@"(?<!\d)(?:\+\d{1,3}[\s.-]?)?(?:\(?\d{2,4}\)?[\s.-]){2,4}\d{2,4}(?!\d)")]
    private static partial Regex PhonePattern();

    /// <summary>13–19 contiguous digits (with optional space/dash grouping) — the ISO/IEC 7812
    /// length range for a payment card number; well beyond any price or specification value this
    /// domain ever states as a single number.</summary>
    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){13,19}(?!\d)")]
    private static partial Regex CreditCardPattern();
}

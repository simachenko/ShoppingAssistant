using System.Text;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// The turn-processing cycle's first stage (spec.md FR-104/FR-107): normalizes the raw user
/// message and rejects it outright — before any language-model call — if it's too long or
/// contains characters that have no place in ordinary text input.
/// </summary>
public static class InputValidationStage
{
    public static string ValidateAndNormalize(string userMessage, RequestGuardrailOptions options)
    {
        var normalized = userMessage.Normalize(NormalizationForm.FormC);

        if (normalized.Length > options.MaxMessageLength)
        {
            // 400: this project doesn't reference ASP.NET Core, so the literal status code is
            // used directly rather than Microsoft.AspNetCore.Http.StatusCodes — interpreted by
            // the API layer, which does.
            throw new GuardrailRejectionException(
                400, $"Message exceeds the maximum length of {options.MaxMessageLength} characters.");
        }

        if (ContainsDisallowedControlCharacters(normalized))
        {
            throw new GuardrailRejectionException(400, "Message contains characters that are not allowed.");
        }

        return normalized;
    }

    /// <summary>Ordinary whitespace (tab, line feed, carriage return) is allowed; every other
    /// Unicode control character is not.</summary>
    private static bool ContainsDisallowedControlCharacters(string text) =>
        text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r'));
}

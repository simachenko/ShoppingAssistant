namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// A Request Guardrail rejection (spec.md FR-104–FR-113): thrown before any LLM/tool call is
/// made for a turn that fails admission control (oversized message, dangerous characters,
/// oversized hard-constraint/preference lists, blocked PII). Caught once at the API boundary and
/// mapped directly to <see cref="StatusCode"/> — never reaches <c>AdvisorTurnResult</c>, since a
/// guardrail violation isn't a completed turn's outcome at all (FR-113: a controlled rejection,
/// zero LLM/tool invocation, never a silent truncation or coercion).
/// </summary>
public sealed class GuardrailRejectionException(int statusCode, string reason) : Exception(reason)
{
    public int StatusCode { get; } = statusCode;
}

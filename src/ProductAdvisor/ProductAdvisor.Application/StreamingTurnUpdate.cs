namespace ProductAdvisor.Application;

/// <summary>
/// One item in a streamed conversation turn (FR-015/research.md §11) — either a narration-text
/// delta (<see cref="Delta"/> set) or, exactly once and always last, the same
/// <see cref="AdvisorTurnResult"/> the non-streaming endpoint would return for this turn
/// (<see cref="Result"/> set). Mirrors the SSE <c>token</c>/<c>result</c> event scheme in
/// contracts/advisor-conversation-api.md.
/// </summary>
public sealed record StreamingTurnUpdate
{
    public string? Delta { get; init; }
    public AdvisorTurnResult? Result { get; init; }

    public static StreamingTurnUpdate ForToken(string delta) => new() { Delta = delta };

    public static StreamingTurnUpdate ForResult(AdvisorTurnResult result) => new() { Result = result };
}

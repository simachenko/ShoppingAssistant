using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>
/// What the orchestrator produced for one conversation turn — always either a clarification
/// question or a tool-produced result (recommendation now, comparison once US2 is wired), never
/// a value the orchestrator computed itself.
/// </summary>
public sealed record AdvisorTurnResult
{
    public required string Type { get; init; }
    public string? Message { get; init; }
    public string? Question { get; init; }
    public Recommendation? Recommendation { get; init; }

    public static AdvisorTurnResult ForClarification(string question) =>
        new() { Type = "clarification", Question = question };

    public static AdvisorTurnResult ForRecommendation(string message, Recommendation recommendation) =>
        new() { Type = "recommendation", Message = message, Recommendation = recommendation };
}

using Microsoft.Extensions.AI;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>
/// The Advisor's entire "semantic UI": feeds the LLM the conversation history and the tool
/// catalog, lets it decide which tool(s) to call, and relays whatever it decided — narration or
/// a tool-produced result. This class performs <b>no product-data computation of its own</b>;
/// every fact/score/rating a user ever sees came from a tool call captured via
/// <see cref="IToolResultCapture"/> (research.md §1, plan.md Summary).
/// </summary>
public sealed class ConversationOrchestrator(
    IChatClient chatClient,
    IAdvisorToolCatalog toolCatalog,
    IToolResultCapture resultCapture)
{
    private const string SystemPrompt = """
        You are a retail product advisor. Help the user find, compare, and check facts about
        products using ONLY the provided tools. Never state a price, availability,
        specification, rating, or comparison delta that did not come from a tool result.
        If you do not yet know the product category and budget, ask exactly one focused
        clarifying question instead of calling get_recommendations. Once you call a tool,
        narrate its result faithfully without adding facts it did not return.
        """;

    public async Task<AdvisorTurnResult> ProcessMessageAsync(
        ConversationSession session, string userMessage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("Message text is required.", nameof(userMessage));
        }

        session.AddMessage(new ConversationMessage("user", userMessage, DateTimeOffset.UtcNow));

        var chatOptions = new ChatOptions { Tools = [.. toolCatalog.GetTools()] };
        var response = await chatClient.GetResponseAsync(BuildChatHistory(session), chatOptions, cancellationToken);

        var narration = response.Text;
        session.AddMessage(new ConversationMessage("assistant", narration, DateTimeOffset.UtcNow));

        if (resultCapture.Recommendation is not null)
        {
            session.UpdateRequirement(resultCapture.RequirementUsed!);
            session.StartRecommending();
            return AdvisorTurnResult.ForRecommendation(narration, resultCapture.Recommendation);
        }

        // No tool call means the LLM decided it didn't have enough information — its own
        // response text is the clarifying question (FR-002/FR-003 — exactly one, since we
        // only ever ask for the single next missing field this turn).
        var question = new ClarificationQuestion("RequirementDetails", narration);
        session.AskClarification(question);
        return AdvisorTurnResult.ForClarification(narration);
    }

    private static List<ChatMessage> BuildChatHistory(ConversationSession session)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
        messages.AddRange(session.Messages.Select(
            m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Text)));
        return messages;
    }
}

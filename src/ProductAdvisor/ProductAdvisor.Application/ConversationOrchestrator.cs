using System.Runtime.CompilerServices;
using System.Text;
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
        When the user asks to compare two or more named products, first resolve their product
        ids (e.g., via search_products) and then call compare_products — do not write your own
        side-by-side comparison, rating, or delta from search/detail results alone; those are
        only ever computed by compare_products.
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

        return FinalizeTurn(session, narration);
    }

    /// <summary>
    /// Streaming sibling of <see cref="ProcessMessageAsync"/> (FR-015/research.md §11) — same
    /// tool-calling/grounding semantics via <see cref="IChatClient.GetStreamingResponseAsync"/>
    /// (the same <see cref="ProductAdvisor.Domain"/>-owning tool handlers still run mid-stream),
    /// but the narration is yielded token-by-token as it arrives, followed by exactly one final
    /// <see cref="StreamingTurnUpdate"/> carrying the same <see cref="AdvisorTurnResult"/> the
    /// non-streaming path would have returned for this turn.
    /// </summary>
    public async IAsyncEnumerable<StreamingTurnUpdate> ProcessMessageStreamAsync(
        ConversationSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("Message text is required.", nameof(userMessage));
        }

        session.AddMessage(new ConversationMessage("user", userMessage, DateTimeOffset.UtcNow));

        var chatOptions = new ChatOptions { Tools = [.. toolCatalog.GetTools()] };
        var narrationBuilder = new StringBuilder();

        await foreach (var update in chatClient.GetStreamingResponseAsync(BuildChatHistory(session), chatOptions, cancellationToken))
        {
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            narrationBuilder.Append(update.Text);
            yield return StreamingTurnUpdate.ForToken(update.Text);
        }

        var narration = narrationBuilder.ToString();
        session.AddMessage(new ConversationMessage("assistant", narration, DateTimeOffset.UtcNow));

        yield return StreamingTurnUpdate.ForResult(FinalizeTurn(session, narration));
    }

    private AdvisorTurnResult FinalizeTurn(ConversationSession session, string narration)
    {
        if (resultCapture.Comparison is not null)
        {
            session.StartComparing();
            return AdvisorTurnResult.ForComparison(narration, resultCapture.Comparison);
        }

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

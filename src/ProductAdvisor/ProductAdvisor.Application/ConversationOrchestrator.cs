using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

/// <summary>
/// The Advisor's turn-processing cycle (spec.md FR-036–FR-059, research.md §20): input
/// validation → structured intent extraction → schema validation → deterministic state merge →
/// policy routing → route-specific handling. Extraction and routing are always run first and are
/// always deterministic-or-model-constrained — the free "let the model pick any tool" loop
/// (research.md §1's original description) now only remains, as a bridge to Phase 10's full
/// tool-recipe scoping, for the <c>compare</c>/<c>checkout</c>/<c>product_fact</c> routes; the
/// <c>recommend</c> route's compute step is already fully deterministic (FR-066). This class
/// performs <b>no product-data computation of its own</b>; every fact/score/rating a user ever
/// sees came from a tool call (research.md §1, plan.md Summary).
/// </summary>
public sealed class ConversationOrchestrator(
    IChatClient chatClient,
    IAdvisorToolCatalog toolCatalog,
    IToolResultCapture resultCapture,
    ExtractionStage extractionStage,
    IRecommendationService recommendationService)
{
    private const string LegacyToolSystemPrompt = """
        You are a retail product advisor. The shopper's request has already been classified as
        needing a comparison, a checkout link, or a specific product fact — use ONLY the provided
        tools to satisfy it. Never state a price, availability, specification, rating, or
        comparison delta that did not come from a tool result.
        When the user asks to compare two or more named products, first resolve their product
        ids (e.g., via search_products) and then call compare_products — do not write your own
        side-by-side comparison, rating, or delta from search/detail results alone; those are
        only ever computed by compare_products.
        When the user asks about a single named product (its price, availability, or a
        characteristic), call search_products with just that name as the free-text query — do
        not ask for its category first. If nothing matches, tell the user the product could not
        be found rather than guessing.
        When the user wants to buy, check out, or get a purchase link for one or more products,
        resolve which product ids they mean (by name or by their position in the most recently
        shown results) and call generate_checkout_link — do not build a link yourself, and ask
        for clarification instead of guessing if you cannot resolve the products.
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

        var (intent, route) = await ClassifyAndRouteAsync(session, userMessage, cancellationToken);

        var result = route switch
        {
            Route.Clarify => HandleClarify(session, intent),
            Route.Smalltalk => await HandleSmalltalkAsync(userMessage, cancellationToken),
            Route.Unsupported => HandleUnsupported(),
            Route.Recommend => await HandleRecommendAsync(session, cancellationToken),
            _ => await RunLegacyToolContinuationAsync(session, route, cancellationToken),
        };

        session.AddMessage(new ConversationMessage(
            "assistant", result.Message ?? result.Question ?? string.Empty, DateTimeOffset.UtcNow));
        return result;
    }

    /// <summary>
    /// Streaming sibling of <see cref="ProcessMessageAsync"/> (FR-015/research.md §11) — the
    /// classification/routing stages above are never streamed (they are not user-facing text);
    /// only the narration that follows is yielded token-by-token, followed by exactly one final
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

        var (intent, route) = await ClassifyAndRouteAsync(session, userMessage, cancellationToken);

        AdvisorTurnResult result;
        switch (route)
        {
            case Route.Clarify:
                result = HandleClarify(session, intent);
                yield return StreamingTurnUpdate.ForToken(result.Question ?? string.Empty);
                break;

            case Route.Unsupported:
                result = HandleUnsupported();
                yield return StreamingTurnUpdate.ForToken(result.Message ?? string.Empty);
                break;

            case Route.Smalltalk:
                {
                    var narrationBuilder = new StringBuilder();
                    await foreach (var update in chatClient.GetStreamingResponseAsync(
                        BuildSmalltalkMessages(userMessage), cancellationToken: cancellationToken))
                    {
                        if (string.IsNullOrEmpty(update.Text))
                        {
                            continue;
                        }

                        narrationBuilder.Append(update.Text);
                        yield return StreamingTurnUpdate.ForToken(update.Text);
                    }

                    result = AdvisorTurnResult.ForAnswer(narrationBuilder.ToString());
                    break;
                }

            case Route.Recommend:
                {
                    var recommendation = await recommendationService.GetRecommendationsAsync(
                        session.CurrentRequirement, cancellationToken);
                    session.StartRecommending();
                    session.SetLastSearchResults(recommendation.Items
                        .Select(i => new SearchResultReference(i.Candidate.ProductId, i.Candidate.Name))
                        .ToList());

                    var narrationBuilder = new StringBuilder();
                    await foreach (var update in chatClient.GetStreamingResponseAsync(
                        BuildNarrateRecommendationMessages(recommendation), cancellationToken: cancellationToken))
                    {
                        if (string.IsNullOrEmpty(update.Text))
                        {
                            continue;
                        }

                        narrationBuilder.Append(update.Text);
                        yield return StreamingTurnUpdate.ForToken(update.Text);
                    }

                    result = AdvisorTurnResult.ForRecommendation(narrationBuilder.ToString(), recommendation);
                    break;
                }

            default: // Compare, Checkout, ProductFact — the Phase-10 tool-recipe bridge.
                {
                    var chatOptions = new ChatOptions { Tools = [.. toolCatalog.GetTools()] };
                    var narrationBuilder = new StringBuilder();
                    await foreach (var update in chatClient.GetStreamingResponseAsync(
                        BuildLegacyChatHistory(session), chatOptions, cancellationToken))
                    {
                        if (string.IsNullOrEmpty(update.Text))
                        {
                            continue;
                        }

                        narrationBuilder.Append(update.Text);
                        yield return StreamingTurnUpdate.ForToken(update.Text);
                    }

                    result = FinalizeLegacyTurn(session, narrationBuilder.ToString(), route);
                    break;
                }
        }

        session.AddMessage(new ConversationMessage(
            "assistant", result.Message ?? result.Question ?? string.Empty, DateTimeOffset.UtcNow));
        yield return StreamingTurnUpdate.ForResult(result);
    }

    /// <summary>
    /// Stages 2–5 of the cycle: structured-intent extraction, schema validation (inside
    /// <see cref="ExtractionStage"/>), deterministic state merge, and policy routing. Shared by
    /// both the streaming and non-streaming entry points since neither of these stages produces
    /// user-facing text.
    /// </summary>
    private async Task<(StructuredIntent? Intent, Route Route)> ClassifyAndRouteAsync(
        ConversationSession session, string userMessage, CancellationToken cancellationToken)
    {
        var intent = await extractionStage.ExtractAsync(session.CurrentRequirement, userMessage, cancellationToken);
        if (intent is null)
        {
            return (null, Route.Clarify);
        }

        if (intent.RequirementPatch is not null)
        {
            session.MergeRequirement(intent.RequirementPatch);
        }

        return (intent, PolicyRouter.SelectRoute(session.CurrentRequirement, intent));
    }

    private static AdvisorTurnResult HandleClarify(ConversationSession session, StructuredIntent? intent)
    {
        var missingField = intent is { MissingFields.Count: > 0 } ? intent.MissingFields[0] : "RequirementDetails";
        var question = BuildClarificationQuestion(missingField, intent);
        session.AskClarification(new ClarificationQuestion(missingField, question));
        return AdvisorTurnResult.ForClarification(question);
    }

    private static string BuildClarificationQuestion(string missingField, StructuredIntent? intent)
    {
        if (intent is null)
        {
            return "I didn't quite catch that — could you rephrase your request?";
        }

        return missingField switch
        {
            "Budget" => "What's your budget for this?",
            "Category" => "What kind of product are you looking for?",
            _ => "Could you tell me a bit more about what you're looking for?",
        };
    }

    private async Task<AdvisorTurnResult> HandleSmalltalkAsync(string userMessage, CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync(
            BuildSmalltalkMessages(userMessage), cancellationToken: cancellationToken);
        return AdvisorTurnResult.ForAnswer(response.Text);
    }

    /// <summary>
    /// Deterministic, zero-language-model-call reply (FR-067's "no product tool" spirit extended
    /// to "no call needed at all" for a fixed, out-of-scope explanation).
    /// </summary>
    private static AdvisorTurnResult HandleUnsupported() =>
        AdvisorTurnResult.ForUnsupported(
            "I can help with finding, comparing, and checking facts about products in our " +
            "catalog — that request is outside what I can do here.");

    private async Task<AdvisorTurnResult> HandleRecommendAsync(ConversationSession session, CancellationToken cancellationToken)
    {
        // Deterministic — the compute step reads only the already-merged CurrentRequirement,
        // never arguments the language model reconstructs itself (FR-066).
        var recommendation = await recommendationService.GetRecommendationsAsync(
            session.CurrentRequirement, cancellationToken);
        session.StartRecommending();
        session.SetLastSearchResults(recommendation.Items
            .Select(i => new SearchResultReference(i.Candidate.ProductId, i.Candidate.Name))
            .ToList());

        var response = await chatClient.GetResponseAsync(
            BuildNarrateRecommendationMessages(recommendation), cancellationToken: cancellationToken);
        return AdvisorTurnResult.ForRecommendation(response.Text, recommendation);
    }

    private async Task<AdvisorTurnResult> RunLegacyToolContinuationAsync(
        ConversationSession session, Route route, CancellationToken cancellationToken)
    {
        var chatOptions = new ChatOptions { Tools = [.. toolCatalog.GetTools()] };
        var response = await chatClient.GetResponseAsync(BuildLegacyChatHistory(session), chatOptions, cancellationToken);
        return FinalizeLegacyTurn(session, response.Text, route);
    }

    /// <summary>
    /// Finalizes a turn handled by the Phase-10 tool-recipe bridge (<c>compare</c>/
    /// <c>checkout</c>/<c>product_fact</c>). A <c>product_fact</c> turn that reaches here without
    /// setting a capture is an honest <c>answer</c> — not a fallback to <c>clarification</c>
    /// (FR-062/FR-063); a <c>compare</c>/<c>checkout</c> turn that couldn't resolve its product
    /// references this turn still asks, honestly, rather than guessing.
    /// </summary>
    private AdvisorTurnResult FinalizeLegacyTurn(ConversationSession session, string narration, Route route)
    {
        if (resultCapture.CheckoutLink is not null)
        {
            return AdvisorTurnResult.ForCheckoutLink(narration, resultCapture.CheckoutLink);
        }

        if (resultCapture.Comparison is not null)
        {
            session.StartComparing();
            session.SetLastSearchResults(resultCapture.Comparison.Rows
                .Select(r => new SearchResultReference(r.Candidate.ProductId, r.Candidate.Name))
                .ToList());
            return AdvisorTurnResult.ForComparison(narration, resultCapture.Comparison);
        }

        if (route == Route.ProductFact)
        {
            return AdvisorTurnResult.ForAnswer(narration);
        }

        var question = new ClarificationQuestion("ProductReference", narration);
        session.AskClarification(question);
        return AdvisorTurnResult.ForClarification(narration);
    }

    private static List<ChatMessage> BuildSmalltalkMessages(string userMessage) =>
    [
        new ChatMessage(ChatRole.System,
            "Reply briefly and naturally to this message. You have no product data for this " +
            "reply — do not state any price, specification, or availability."),
        new ChatMessage(ChatRole.User, userMessage),
    ];

    private static List<ChatMessage> BuildNarrateRecommendationMessages(Recommendation recommendation) =>
    [
        new ChatMessage(ChatRole.System,
            "Narrate the following already-computed recommendation result faithfully. Do not " +
            "add, alter, or omit any fact, price, or trade-off it does not already contain."),
        new ChatMessage(ChatRole.User, JsonSerializer.Serialize(recommendation)),
    ];

    private static List<ChatMessage> BuildLegacyChatHistory(ConversationSession session)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, LegacyToolSystemPrompt) };

        // Gives the LLM a reliable, structured source for ordinal follow-ups ("the first two",
        // "the cheaper one") instead of requiring it to re-derive product ids from prior prose
        // (FR-022, research.md §15) — the LLM still does the (legitimate) language-understanding
        // work of matching the reference to a position, but the list itself is exact.
        if (session.LastSearchResults.Count > 0)
        {
            var shown = string.Join(
                "\n", session.LastSearchResults.Select((r, i) => $"{i + 1}. {r.Name} (id: {r.ProductId})"));
            messages.Add(new ChatMessage(ChatRole.System,
                $"""
                The most recently shown products, in this order, are:
                {shown}
                If the user refers to them ordinally or descriptively (e.g. "the first two", "the
                cheaper one"), resolve to these exact ids rather than asking again or guessing.
                """));
        }

        messages.AddRange(session.Messages.Select(
            m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Text)));
        return messages;
    }
}

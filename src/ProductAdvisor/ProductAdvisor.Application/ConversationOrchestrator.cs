using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
public sealed partial class ConversationOrchestrator(
    IChatClient chatClient,
    IAdvisorToolCatalog toolCatalog,
    IToolResultCapture resultCapture,
    ExtractionStage extractionStage,
    IRecommendationService recommendationService,
    TurnResourceBudgetGuard budgetGuard,
    RequestGuardrailOptions guardrailOptions,
    TurnMetrics metrics,
    ILogger<ConversationOrchestrator> logger)
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

        var admittedMessage = AdmitMessage(userMessage);
        session.AddMessage(new ConversationMessage("user", admittedMessage, DateTimeOffset.UtcNow));

        AdvisorTurnResult result;
        try
        {
            result = await budgetGuard.RunAsync(async ct =>
            {
                var (intent, route) = await ClassifyAndRouteAsync(session, admittedMessage, ct);
                return route switch
                {
                    Route.Clarify => HandleClarify(session, intent),
                    Route.Smalltalk => await HandleSmalltalkAsync(admittedMessage, ct),
                    Route.Unsupported => HandleUnsupported(),
                    Route.Recommend => await HandleRecommendAsync(session, ct),
                    _ => await RunLegacyToolContinuationAsync(session, route, ct),
                };
            }, cancellationToken);
        }
        catch (TurnBudgetExceededException ex)
        {
            result = AdvisorTurnResult.ForError(ex.Message, ex.Degraded);
        }

        session.AddMessage(new ConversationMessage(
            "assistant", result.Message ?? result.Question ?? string.Empty, DateTimeOffset.UtcNow));
        return result;
    }

    /// <summary>
    /// Stages 0–1 of the turn-processing cycle (spec.md FR-104/FR-107/FR-114/FR-116):
    /// normalizes and screens the raw message before it — or anything derived from it — ever
    /// reaches session state or an LLM call. Throws <see cref="GuardrailRejectionException"/> on
    /// any violation (an oversized/dangerous message, or a blocked-PII message); a redacted
    /// message never has its original raw text persisted or sent onward — only
    /// <see cref="PiiScreeningResult.RedactedText"/> proceeds (FR-116).
    /// </summary>
    private string AdmitMessage(string userMessage)
    {
        var normalized = InputValidationStage.ValidateAndNormalize(userMessage, guardrailOptions);
        var piiResult = PiiScreeningStage.Screen(normalized);

        if (!piiResult.Flagged)
        {
            return normalized;
        }

        metrics.PiiDetection.Add(1);

        if (piiResult.Action == "Blocked")
        {
            throw new GuardrailRejectionException(400, "This message could not be processed.");
        }

        return piiResult.RedactedText ?? normalized;
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

        var admittedMessage = AdmitMessage(userMessage);
        session.AddMessage(new ConversationMessage("user", admittedMessage, DateTimeOffset.UtcNow));

        var (intent, route) = await ClassifyAndRouteAsync(session, admittedMessage, cancellationToken);

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
                        BuildSmalltalkMessages(admittedMessage), cancellationToken: cancellationToken))
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

                    // Buffered rather than token-streamed: output validation's grounding check
                    // (FR-088) must see the complete narration before any of it reaches the
                    // client — a fabricated value can't be un-sent once streamed. This trades
                    // per-token streaming for this one route for the same grounding guarantee
                    // the non-streaming path gets; not fixed by spec.md, which leaves narration
                    // delivery granularity an implementation detail.
                    var narration = await NarrateRecommendationAsync(session.CurrentRequirement, recommendation, cancellationToken);
                    yield return StreamingTurnUpdate.ForToken(narration);
                    result = AdvisorTurnResult.ForRecommendation(narration, recommendation);
                    break;
                }

            default: // Compare, Checkout, ProductFact — the tool-recipe bridge (FR-066–FR-070).
                {
                    // The turn-level resource budget (overall timeout, max tool calls, max
                    // consecutive tool errors) is enforced for the non-streaming entry point
                    // only — data-model.md's TurnResourceBudget explicitly allows the streaming
                    // endpoint's fail-safe on overall timeout to be "no result event" rather than
                    // a gracefully streamed `error`, since a mid-stream `yield return` cannot sit
                    // inside a try/catch. The tool-recipe scoping below still applies equally.
                    var chatOptions = new ChatOptions { Tools = [.. toolCatalog.GetTools(route)] };
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
        var intent = await extractionStage.ExtractAsync(
            session.CurrentRequirement, session.LastSearchResults, userMessage, cancellationToken);
        if (intent is null)
        {
            LogTurnClassified(ExtractionStage.PromptVersion, Route.Clarify);
            return (null, Route.Clarify);
        }

        if (intent.RequirementPatch is not null)
        {
            // FR-106: checked against what the merge would actually produce, before it happens —
            // an oversized patch is rejected outright (400), never silently truncated or merged.
            RequirementPatchGuardrails.EnsureWithinLimits(session.CurrentRequirement, intent.RequirementPatch, guardrailOptions);
            session.MergeRequirement(intent.RequirementPatch);
        }

        var route = PolicyRouter.SelectRoute(session.CurrentRequirement, intent);
        LogTurnClassified(ExtractionStage.PromptVersion, route);
        return (intent, route);
    }

    /// <summary>FR-101: the exact prompt version behind a turn's classification/narration must be
    /// runtime-observable, not only inferable from source-control history.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Turn classified: extractionPromptVersion={ExtractionPromptVersion} route={Route}")]
    private partial void LogTurnClassified(string extractionPromptVersion, Route route);

    [LoggerMessage(Level = LogLevel.Information, Message = "Narration produced: narrationPromptVersion={NarrationPromptVersion}")]
    private partial void LogNarrationProduced(string narrationPromptVersion);

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

        var narration = await NarrateRecommendationAsync(session.CurrentRequirement, recommendation, cancellationToken);
        return AdvisorTurnResult.ForRecommendation(narration, recommendation);
    }

    /// <summary>
    /// The turn-processing cycle's constrained-narration stage for `recommend` (spec.md
    /// FR-086/FR-087): builds an <see cref="EvidenceEnvelope"/> from the already-computed
    /// <see cref="Recommendation"/>, narrates from that Envelope alone (never the raw
    /// <see cref="Recommendation"/> object), then runs output validation's grounding check
    /// (FR-088–FR-090) before the narration is used anywhere. This is the one route where FR-087
    /// is fully met today — see <see cref="ApplyGroundingIfApplicable"/> for the narrower,
    /// post-hoc treatment the legacy `compare`/`checkout` bridge gets instead.
    /// </summary>
    private async Task<string> NarrateRecommendationAsync(
        UserRequirement requirement, Recommendation recommendation, CancellationToken cancellationToken)
    {
        var envelope = EvidenceEnvelopeBuilder.ForRecommendation(requirement, recommendation);
        var response = await chatClient.GetResponseAsync(
            NarrationPrompt.BuildMessages(requirement, envelope), cancellationToken: cancellationToken);
        LogNarrationProduced(NarrationPrompt.PromptVersion);

        var validated = OutputValidationStage.Validate(response.Text, envelope);
        if (!string.Equals(validated, response.Text, StringComparison.Ordinal))
        {
            metrics.GroundingFailure.Add(1);
        }

        return validated;
    }

    private async Task<AdvisorTurnResult> RunLegacyToolContinuationAsync(
        ConversationSession session, Route route, CancellationToken cancellationToken)
    {
        // FR-066–FR-070: never the full seven-tool catalog — only this route's fixed recipe.
        var chatOptions = new ChatOptions { Tools = [.. toolCatalog.GetTools(route)] };

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(BuildLegacyChatHistory(session), chatOptions, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            // The shared chat client's own MaximumConsecutiveErrorsPerRequest budget was
            // exhausted — the underlying tool exception propagates as-is out of
            // GetResponseAsync (see TurnResourceBudgetGuard). A persistently failing tool is an
            // honest, retryable turn failure (FR-079), not silently swallowed.
            throw new TurnBudgetExceededException(
                "One of the tools this request needed failed repeatedly and could not complete.", degraded: true);
        }

        if (TurnResourceBudgetGuard.ExceededToolCallBudget(response))
        {
            metrics.LoopLimitReached.Add(1);
            throw new TurnBudgetExceededException(
                "This request needed more steps than allowed and could not be completed.", degraded: true);
        }

        return ApplyGroundingIfApplicable(FinalizeLegacyTurn(session, response.Text, route));
    }

    /// <summary>
    /// Post-hoc grounding validation (FR-088–FR-090) for the legacy tool-invocation bridge's
    /// `comparison`/`checkoutLink` results — applied only from the non-streaming entry point,
    /// since the streaming entry point has already sent narration tokens to the client by the
    /// time this would run. This is a narrower guarantee than FR-087's "narration call receives
    /// ONLY the Envelope" (this bridge's single LLM call still sees raw tool output while
    /// deciding what to say) — a known, documented gap pending this bridge's full replacement by
    /// Phase 10's tool-recipe-scoped, two-step flow; what this DOES fully deliver is FR-088's
    /// promise that no ungrounded claim reaches the user, applied after the fact to whatever
    /// narration the bridge produced. `product_fact`/`smalltalk` are deliberately excluded: with
    /// no structured capture to build an Envelope from, an empty-claims check would reject every
    /// legitimate fact these routes state, which would be a regression, not a safety
    /// improvement.
    /// </summary>
    private AdvisorTurnResult ApplyGroundingIfApplicable(AdvisorTurnResult result)
    {
        var envelope = (result.Type, result.Comparison, result.CheckoutLink) switch
        {
            ("comparison", { } comparison, _) => EvidenceEnvelopeBuilder.ForComparison(comparison),
            ("checkoutLink", _, { } checkoutLink) => EvidenceEnvelopeBuilder.ForCheckoutLink(checkoutLink),
            _ => null,
        };

        if (envelope is null || result.Message is null)
        {
            return result;
        }

        var validated = OutputValidationStage.Validate(result.Message, envelope);
        if (string.Equals(validated, result.Message, StringComparison.Ordinal))
        {
            return result;
        }

        metrics.GroundingFailure.Add(1);
        return result with { Message = validated };
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

    private List<ChatMessage> BuildLegacyChatHistory(ConversationSession session)
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

        // FR-112: bounds what's *included* in this prompt only — the full transcript remains
        // persisted and available via the conversation view (FR-023) regardless of this limit.
        messages.AddRange(session.Messages
            .TakeLast(guardrailOptions.MaxActiveContextMessages)
            .Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Text)));
        return messages;
    }
}

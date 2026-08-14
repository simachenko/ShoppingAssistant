using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector.EntityFrameworkCore;
using ProductAdvisor.Application;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure;
using ProductAdvisor.Infrastructure.Rag;
using ProductAdvisor.Infrastructure.Repositories;
using ProductAdvisor.Infrastructure.SeedData;
using ProductAdvisor.Infrastructure.Tools;

var builder = WebApplication.CreateBuilder(args);

// FR-105: rejected before the body is even parsed — Kestrel itself returns 413 (via
// BadHttpRequestException, already handled by GlobalExceptionHandler) once this is exceeded.
var maxRequestBodyBytes = builder.Configuration.GetValue(
    $"{RequestGuardrailOptions.SectionName}:MaxRequestBodyBytes", new RequestGuardrailOptions().MaxRequestBodyBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

builder.AddServiceDefaults();
// UseVector() registers pgvector's Npgsql type mapping — without it the `vector` column type the
// DocumentChunk mapping declares cannot be read or written (002 research.md §6).
builder.AddNpgsqlDbContext<AdvisorDbContext>(
    "advisordb", configureDbContextOptions: o => o.UseNpgsql(npgsql => npgsql.UseVector()));
builder.AddAdvisorChatClient();
builder.AddAdvisorEmbeddingGenerator();
builder.AddAdvisorHttpClients();

builder.Services.AddSingleton<TurnMetrics>();
// FR-136: exposes the seven turn-cycle metrics through the same OpenTelemetry pipeline every
// other metric in this service already uses (ConfigureOpenTelemetry, research.md §16) — additive
// to that shared configuration, not a second, separate metrics pipeline.
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(TurnMetrics.MeterName));
builder.Services.AddScoped<IToolResultCapture, ToolResultCapture>();
builder.Services.AddScoped<ProductComparisonService>();
builder.Services.AddScoped<DataAccessTools>();
builder.Services.AddScoped<ComputeTools>();
builder.Services.AddScoped<RagTools>();
builder.Services.AddScoped<IAdvisorToolCatalog, AdvisorToolCatalog>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.Configure<StoreInfoOptions>(builder.Configuration.GetSection(StoreInfoOptions.SectionName));
builder.Services.AddSingleton<IStoreContext, ConfiguredStoreContext>();
builder.Services.AddScoped<IStoreInfoRetrievalService, StoreInfoRetrievalService>();
builder.Services.AddScoped<ExtractionStage>();
builder.Services.Configure<TurnResourceBudgetOptions>(builder.Configuration.GetSection(TurnResourceBudgetOptions.SectionName));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TurnResourceBudgetOptions>>().Value);
builder.Services.AddScoped<TurnResourceBudgetGuard>();
builder.Services.Configure<RequestGuardrailOptions>(builder.Configuration.GetSection(RequestGuardrailOptions.SectionName));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestGuardrailOptions>>().Value);
builder.Services.AddScoped<ConversationOrchestrator>();
builder.Services.AddScoped<IConversationSessionRepository, ConversationSessionRepository>();
builder.Services.AddSingleton<ConversationTurnGate>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<DataAccessTools>()
    .WithTools<ComputeTools>()
    .WithTools<RagTools>();

var app = builder.Build();

// Correlation id must wrap the exception handler, not the other way around — otherwise an
// unhandled exception's own log line is written after the correlation-id scope has already
// unwound and never carries it (FR-027, research.md §7/§16).
app.UseCorrelationId();
app.UseExceptionHandler();
app.UseInternalApiKeyAuth();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
    await db.Database.MigrateAsync();

    // Demo knowledge base (002 research.md §12) — same opt-in flag and idempotent shape as
    // Catalog/Pricing's own demo seeding. A seeding failure (e.g. the embedding provider is
    // unreachable at startup) must not stop the service: store-policy answers then honestly
    // report finding nothing (FR-009), while every product capability keeps working.
    if (app.Configuration.GetValue<bool>("SeedDemoData"))
    {
        try
        {
            await StoreInfoSeeder.SeedAsync(
                db,
                scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                scope.ServiceProvider.GetRequiredService<IStoreContext>().CurrentStoreId);
        }
#pragma warning disable CA1031 // Intentional: demo seeding must never block startup (see above).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // CA1848 (LoggerMessage delegates) does not apply usefully to a startup path that runs
            // exactly once, and top-level statements cannot host the source-generated partial.
#pragma warning disable CA1848
            app.Logger.LogWarning(ex, "Store-info demo seeding skipped: the knowledge base will be empty.");
#pragma warning restore CA1848
        }
    }
}

// POST /api/conversations — start a new session (contracts/advisor-conversation-api.md).
// X-User-Id is Gateway's already-validated caller identity (research.md §17), trusted here only
// because the internal API key already establishes the caller is Gateway (FR-031).
app.MapPost("/api/conversations", async (
    [FromHeader(Name = "X-User-Id")] string? userId,
    IConversationSessionRepository repository,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    var session = new ConversationSession(Guid.NewGuid(), userId);
    await repository.AddAsync(session, ct);
    await repository.SaveChangesAsync(ct);
    return Results.Created($"/api/conversations/{session.SessionId}", new { sessionId = session.SessionId });
});

// POST /api/conversations/{sessionId}/messages — one chat turn
app.MapPost("/api/conversations/{sessionId:guid}/messages", async (
    Guid sessionId,
    [FromHeader(Name = "X-User-Id")] string? userId,
    SendMessageRequest request,
    IConversationSessionRepository repository,
    ConversationOrchestrator orchestrator,
    ConversationTurnGate turnGate,
    RequestGuardrailOptions guardrailOptions,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    // FR-104/FR-107/FR-116: rejected before a turn is even started — before the session is even
    // looked up, so a guardrail-doomed request never touches the turn gate either.
    if (TryRejectByGuardrail(request.Text, guardrailOptions) is { } rejection)
    {
        return rejection;
    }

    var session = await repository.GetAsync(sessionId, ct);
    if (!IsOwnedBy(session, userId))
    {
        // A non-owner (or a genuinely unknown id) gets the identical 404 either way — this
        // must never confirm to a non-owner that the session id exists at all (FR-031).
        return Results.NotFound();
    }

    // FR-024/SC-014: a second message for this session while one is already being processed is
    // rejected, never processed concurrently with the first.
    if (!turnGate.TryEnter(sessionId))
    {
        return Results.Conflict("A turn for this session is already being processed.");
    }

    try
    {
        var turnResult = await orchestrator.ProcessMessageAsync(session!, request.Text, ct);
        await repository.SaveChangesAsync(ct);

        return Results.Ok(ConversationApiMapper.ToResponse(turnResult));
    }
    catch (GuardrailRejectionException ex)
    {
        // FR-106: only detectable after extraction runs (it validates the extracted
        // requirementPatch, not the raw message) — still zero tool calls made past this point.
        return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
    }
    finally
    {
        turnGate.Exit(sessionId);
    }
});

// Shared pre-check (spec.md FR-104/FR-107/FR-116) for both the streaming and non-streaming
// message endpoints — cheap and synchronous, so it runs before any session lookup, turn-gate
// acquisition, or LLM call. FR-106 (requirementPatch list limits) can only be checked once
// extraction has produced a patch, so it is not covered here — see each endpoint's own handling.
static IResult? TryRejectByGuardrail(string rawMessage, RequestGuardrailOptions guardrailOptions)
{
    try
    {
        var normalized = InputValidationStage.ValidateAndNormalize(rawMessage, guardrailOptions);
        var piiResult = PiiScreeningStage.Screen(normalized);
        return piiResult is { Flagged: true, Action: "Blocked" }
            ? Results.Problem(detail: "This message could not be processed.", statusCode: 400)
            : null;
    }
    catch (GuardrailRejectionException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
    }
}

// POST /api/conversations/{sessionId}/messages/stream — streaming sibling of the endpoint above
// (FR-015, contracts/advisor-conversation-api.md): narration arrives as `token` SSE events, then
// exactly one `result` event carries the same ConversationTurnResponse the non-streaming
// endpoint would have returned for this turn.
app.MapPost("/api/conversations/{sessionId:guid}/messages/stream", async Task<IResult> (
    Guid sessionId,
    [FromHeader(Name = "X-User-Id")] string? userId,
    SendMessageRequest request,
    IConversationSessionRepository repository,
    ConversationOrchestrator orchestrator,
    ConversationTurnGate turnGate,
    RequestGuardrailOptions guardrailOptions,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    // Checked here, before the SSE response starts (and headers commit to 200) — a guardrail
    // exception raised later, inside the stream itself (e.g. FR-106, only detectable after
    // extraction), can no longer become a clean status code; this pre-check is what makes the
    // common guardrail cases (FR-104/FR-107/FR-116) still return one for the streaming endpoint.
    if (TryRejectByGuardrail(request.Text, guardrailOptions) is { } rejection)
    {
        return rejection;
    }

    var session = await repository.GetAsync(sessionId, ct);
    if (!IsOwnedBy(session, userId))
    {
        return Results.NotFound();
    }

    // FR-024/SC-014: same guard as the non-streaming endpoint. The gate stays entered for the
    // whole stream's lifetime, released inside StreamTurnAsync's finally block once the stream
    // ends (successfully, on error, or if the client disconnects early).
    if (!turnGate.TryEnter(sessionId))
    {
        return Results.Conflict("A turn for this session is already being processed.");
    }

    return TypedResults.ServerSentEvents(StreamTurnAsync(session!, request.Text, orchestrator, repository, turnGate, ct));
});

// True only when the session exists AND belongs to the given user — a missing/mismatched
// X-User-Id and a genuinely unknown sessionId are indistinguishable to the caller (FR-031).
static bool IsOwnedBy(ConversationSession? session, string? userId) =>
    session is not null && !string.IsNullOrEmpty(userId) && session.UserId == userId;

static async IAsyncEnumerable<SseItem<string>> StreamTurnAsync(
    ConversationSession session,
    string text,
    ConversationOrchestrator orchestrator,
    IConversationSessionRepository repository,
    ConversationTurnGate turnGate,
    [EnumeratorCancellation] CancellationToken ct)
{
    try
    {
        await foreach (var update in orchestrator.ProcessMessageStreamAsync(session, text, ct))
        {
            if (update.Delta is not null)
            {
                yield return new SseItem<string>(
                    JsonSerializer.Serialize(new { delta = update.Delta }, SseJson.Options), "token");
            }
            else
            {
                await repository.SaveChangesAsync(ct);
                yield return new SseItem<string>(
                    JsonSerializer.Serialize(ConversationApiMapper.ToResponse(update.Result!), SseJson.Options), "result");
            }
        }
    }
    finally
    {
        turnGate.Exit(session.SessionId);
    }
}

// POST /api/comparisons — stateless, non-conversational comparison (FR-018, research.md §14):
// no sessionId, no conversation turn, no LLM tool-selection step. Calls the same shared
// ProductComparisonService the compare_products MCP tool uses, so results for the same
// productIds are byte-identical regardless of which path invoked them (SC-010).
app.MapPost("/api/comparisons", async Task<IResult> (
    DirectComparisonRequest request,
    ProductComparisonService comparisonService,
    IChatClient chatClient,
    CancellationToken ct) =>
{
    Comparison comparison;
    try
    {
        comparison = await comparisonService.CompareAsync(request.ProductIds, ct);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(ex.Message);
    }

    var (criteria, rows) = ConversationApiMapper.ToComparisonParts(comparison);

    var explanation = request.IncludeExplanation
        ? await TryGenerateExplanationAsync(chatClient, criteria, rows, ct)
        : null;

    return Results.Ok(new DirectComparisonResponse(criteria, rows, explanation));
});

static async Task<string?> TryGenerateExplanationAsync(
    IChatClient chatClient, IReadOnlyList<string> criteria, IReadOnlyList<ComparisonRowResponse> rows, CancellationToken ct)
{
    // A separate, narrowly-scoped call whose only input is the already-computed table — it can
    // only narrate, never alter, invent, or omit a value (FR-019). Any failure here (provider
    // down, timeout, disabled) must never fail the comparison itself, so every exception
    // collapses to "no explanation" rather than a 5xx (constitution Principle V).
    try
    {
        var payload = JsonSerializer.Serialize(new { criteria, rows });
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, """
                You summarize an already-computed product comparison table for a shopper. Write a
                short (2-4 sentence) factual summary of the most notable differences. You MUST NOT
                invent, alter, recompute, or omit any value from the data given to you — restate
                only what is present.
                """),
            new(ChatRole.User, payload),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return string.IsNullOrWhiteSpace(response.Text) ? null : response.Text;
    }
#pragma warning disable CA1031 // Intentional: narration failure must never fail the comparison response (FR-019).
    catch (Exception)
#pragma warning restore CA1031
    {
        return null;
    }
}

// GET /api/conversations/{sessionId} — full transcript + current requirement snapshot
app.MapGet("/api/conversations/{sessionId:guid}", async (
    Guid sessionId,
    [FromHeader(Name = "X-User-Id")] string? userId,
    IConversationSessionRepository repository,
    CancellationToken ct) =>
{
    var session = await repository.GetAsync(sessionId, ct);
    return IsOwnedBy(session, userId) ? Results.Ok(ConversationApiMapper.ToSnapshot(session!)) : Results.NotFound();
});

// DELETE /api/conversations/{sessionId} — user-initiated deletion of one session (FR-119).
// Same ownership check and 404 as every other session-scoped endpoint; a turn already in flight
// for this session is a 409, mirroring FR-024's own conflict response, rather than deleting out
// from under an in-progress turn.
app.MapDelete("/api/conversations/{sessionId:guid}", async (
    Guid sessionId,
    [FromHeader(Name = "X-User-Id")] string? userId,
    IConversationSessionRepository repository,
    ConversationTurnGate turnGate,
    CancellationToken ct) =>
{
    var session = await repository.GetAsync(sessionId, ct);
    if (!IsOwnedBy(session, userId))
    {
        return Results.NotFound();
    }

    if (!turnGate.TryEnter(sessionId))
    {
        return Results.Conflict("A turn for this session is already being processed.");
    }

    try
    {
        await repository.DeleteAsync(sessionId, ct);
        return Results.NoContent();
    }
    finally
    {
        turnGate.Exit(sessionId);
    }
});

// DELETE /api/conversations — user-initiated deletion of every one of the caller's sessions
// (FR-119). No per-session turn-gate check here (there is no single sessionId to hold) — a turn
// concurrently in flight for one of this user's sessions may still complete against
// already-deleted state; this is the same class of race the per-session concurrency limit
// (FR-110, not yet implemented) is meant to bound, not something this endpoint alone can close.
app.MapDelete("/api/conversations", async (
    [FromHeader(Name = "X-User-Id")] string? userId,
    IConversationSessionRepository repository,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    await repository.DeleteAllForUserAsync(userId, ct);
    return Results.NoContent();
});

app.Run();

// Matches the camelCase field names (`type`, `message`, `items`, ...) that ASP.NET Core's
// Results.Ok(...) already produces for the non-streaming endpoint (Web JSON defaults) — the SSE
// path serializes manually, so it must opt into the same casing or a strongly-typed client
// deserializing the `result` event (e.g. the Blazor UI) silently gets default(T) for every field.
file static class SseJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public partial class Program;

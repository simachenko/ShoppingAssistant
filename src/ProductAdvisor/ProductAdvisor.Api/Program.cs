using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ProductAdvisor.Application;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure;
using ProductAdvisor.Infrastructure.Repositories;
using ProductAdvisor.Infrastructure.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AdvisorDbContext>("advisordb");
builder.AddAdvisorChatClient();
builder.AddAdvisorHttpClients();

builder.Services.AddScoped<IToolResultCapture, ToolResultCapture>();
builder.Services.AddScoped<ProductComparisonService>();
builder.Services.AddScoped<DataAccessTools>();
builder.Services.AddScoped<ComputeTools>();
builder.Services.AddScoped<IAdvisorToolCatalog, AdvisorToolCatalog>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<ExtractionStage>();
builder.Services.Configure<TurnResourceBudgetOptions>(builder.Configuration.GetSection(TurnResourceBudgetOptions.SectionName));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TurnResourceBudgetOptions>>().Value);
builder.Services.AddScoped<TurnResourceBudgetGuard>();
builder.Services.AddScoped<ConversationOrchestrator>();
builder.Services.AddScoped<IConversationSessionRepository, ConversationSessionRepository>();
builder.Services.AddSingleton<ConversationTurnGate>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<DataAccessTools>()
    .WithTools<ComputeTools>();

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
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
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
    finally
    {
        turnGate.Exit(sessionId);
    }
});

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
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
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

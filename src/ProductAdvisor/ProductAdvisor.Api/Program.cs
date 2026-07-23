using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ProductAdvisor.Application;
using ProductAdvisor.Application.Contracts;
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
builder.Services.AddScoped<ConversationOrchestrator>();
builder.Services.AddScoped<IConversationSessionRepository, ConversationSessionRepository>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<DataAccessTools>()
    .WithTools<ComputeTools>();

var app = builder.Build();

app.UseCorrelationId();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
    await db.Database.MigrateAsync();
}

// POST /api/conversations — start a new session (contracts/advisor-conversation-api.md)
app.MapPost("/api/conversations", async (IConversationSessionRepository repository, CancellationToken ct) =>
{
    var session = new ConversationSession(Guid.NewGuid());
    await repository.AddAsync(session, ct);
    await repository.SaveChangesAsync(ct);
    return Results.Created($"/api/conversations/{session.SessionId}", new { sessionId = session.SessionId });
});

// POST /api/conversations/{sessionId}/messages — one chat turn
app.MapPost("/api/conversations/{sessionId:guid}/messages", async (
    Guid sessionId,
    SendMessageRequest request,
    IConversationSessionRepository repository,
    ConversationOrchestrator orchestrator,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    var session = await repository.GetAsync(sessionId, ct);
    if (session is null)
    {
        return Results.NotFound();
    }

    var turnResult = await orchestrator.ProcessMessageAsync(session, request.Text, ct);
    await repository.SaveChangesAsync(ct);

    return Results.Ok(ConversationApiMapper.ToResponse(turnResult));
});

// POST /api/conversations/{sessionId}/messages/stream — streaming sibling of the endpoint above
// (FR-015, contracts/advisor-conversation-api.md): narration arrives as `token` SSE events, then
// exactly one `result` event carries the same ConversationTurnResponse the non-streaming
// endpoint would have returned for this turn.
app.MapPost("/api/conversations/{sessionId:guid}/messages/stream", async Task<IResult> (
    Guid sessionId,
    SendMessageRequest request,
    IConversationSessionRepository repository,
    ConversationOrchestrator orchestrator,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    var session = await repository.GetAsync(sessionId, ct);
    if (session is null)
    {
        return Results.NotFound();
    }

    return TypedResults.ServerSentEvents(StreamTurnAsync(session, request.Text, orchestrator, repository, ct));
});

static async IAsyncEnumerable<SseItem<string>> StreamTurnAsync(
    ConversationSession session,
    string text,
    ConversationOrchestrator orchestrator,
    IConversationSessionRepository repository,
    [EnumeratorCancellation] CancellationToken ct)
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
    Guid sessionId, IConversationSessionRepository repository, CancellationToken ct) =>
{
    var session = await repository.GetAsync(sessionId, ct);
    return session is null ? Results.NotFound() : Results.Ok(ConversationApiMapper.ToSnapshot(session));
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

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

// GET /api/conversations/{sessionId} — full transcript + current requirement snapshot
app.MapGet("/api/conversations/{sessionId:guid}", async (
    Guid sessionId, IConversationSessionRepository repository, CancellationToken ct) =>
{
    var session = await repository.GetAsync(sessionId, ct);
    return session is null ? Results.NotFound() : Results.Ok(ConversationApiMapper.ToSnapshot(session));
});

app.Run();

public partial class Program;

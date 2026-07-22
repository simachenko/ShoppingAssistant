using System.Text.Json;
using Gateway.Api;
using Gateway.Api.Clients;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddHttpClient<AdvisorApiClient>(client => client.BaseAddress = new Uri("http://advisor-api"));

var app = builder.Build();

app.UseCorrelationId();
app.MapDefaultEndpoints();
app.MapReverseProxy();

// POST /api/chat/messages — starts a session if none given, forwards the message, and merges
// the resolved sessionId into Advisor's (otherwise pass-through) response (contracts/gateway-bff-api.md).
app.MapPost("/api/chat/messages", async (ChatMessageRequest request, AdvisorApiClient advisor, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    var sessionId = request.SessionId ?? await advisor.CreateSessionAsync(ct);
    var turn = await advisor.SendMessageAsync(sessionId, request.Text, ct);

    var merged = new Dictionary<string, object?> { ["sessionId"] = sessionId };
    foreach (var property in turn.EnumerateObject())
    {
        merged[property.Name] = property.Value.Clone();
    }

    return Results.Ok(merged);
});

// GET /api/chat/{sessionId} — pass-through of the Advisor conversation snapshot.
app.MapGet("/api/chat/{sessionId:guid}", async (Guid sessionId, AdvisorApiClient advisor, CancellationToken ct) =>
{
    var snapshot = await advisor.GetSnapshotAsync(sessionId, ct);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot.Value);
});

app.Run();

public partial class Program;

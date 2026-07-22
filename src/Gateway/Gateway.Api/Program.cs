using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gateway.Api;
using Gateway.Api.Clients;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
builder.Services.AddHttpClient<AdvisorApiClient>(client => client.BaseAddress = new Uri("http://advisor-api"))
    // ServiceDefaults' standard resilience handler assumes short request/response calls (10s
    // per-attempt, 30s total, with retries) — wrong on two counts for the SSE streaming call:
    // a healthy in-progress stream can legitimately run past those windows, and retrying a
    // streaming POST would silently re-run a non-idempotent conversation turn. Replace it with
    // a single generous timeout and no retry for this client.
    .RemoveAllResilienceHandlers()
    .AddResilienceHandler("advisor-streaming", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(5)));
#pragma warning restore EXTEXP0001

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

// POST /api/chat/messages/stream — streaming sibling of the endpoint above
// (contracts/gateway-bff-api.md): proxies the Advisor's SSE stream, merging the resolved
// sessionId into the final `result` event so the caller never needs a second round trip.
app.MapPost("/api/chat/messages/stream", async Task<IResult> (ChatMessageRequest request, AdvisorApiClient advisor, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    var sessionId = request.SessionId ?? await advisor.CreateSessionAsync(ct);
    return TypedResults.ServerSentEvents(StreamChatAsync(sessionId, request.Text, advisor, ct));
});

static async IAsyncEnumerable<SseItem<string>> StreamChatAsync(
    Guid sessionId, string text, AdvisorApiClient advisor, [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var (eventType, data) in advisor.StreamMessageAsync(sessionId, text, ct))
    {
        if (eventType != "result")
        {
            yield return new SseItem<string>(data, eventType);
            continue;
        }

        using var doc = JsonDocument.Parse(data);
        var merged = new Dictionary<string, object?> { ["sessionId"] = sessionId };
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            merged[property.Name] = property.Value.Clone();
        }

        yield return new SseItem<string>(JsonSerializer.Serialize(merged), "result");
    }
}

// GET /api/chat/{sessionId} — pass-through of the Advisor conversation snapshot.
app.MapGet("/api/chat/{sessionId:guid}", async (Guid sessionId, AdvisorApiClient advisor, CancellationToken ct) =>
{
    var snapshot = await advisor.GetSnapshotAsync(sessionId, ct);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot.Value);
});

app.Run();

public partial class Program;

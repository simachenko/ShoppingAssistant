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

builder.Services.AddHttpClient<CatalogApiClient>(client => client.BaseAddress = new Uri("http://catalog-api"));
builder.Services.AddHttpClient<PricingApiClient>(client => client.BaseAddress = new Uri("http://pricing-api"));

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

// GET /api/products/search — explicit product-picker composition (FR-020, contracts/gateway-bff-api.md):
// no chat, no LLM involvement at all. Catalog narrows by category/text/characteristics, then
// this endpoint batch-fetches Pricing offers for that candidate set and filters/sorts/limits by
// price range on top (research.md §13's pushdown-composition pattern, applied to a list).
app.MapGet("/api/products/search", async Task<IResult> (
    string? category,
    Guid? categoryId,
    string? q,
    string[]? characteristics,
    decimal? priceMin,
    decimal? priceMax,
    string? sortBy,
    int? page,
    int? pageSize,
    CatalogApiClient catalog,
    PricingApiClient pricing,
    CancellationToken ct) =>
{
    List<CatalogCharacteristicFilterDto> parsedCharacteristics;
    try
    {
        parsedCharacteristics = ParseCharacteristics(characteristics);
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    CatalogSearchResponse searchResult;
    try
    {
        var searchRequest = new CatalogSearchRequest(
            categoryId, category, q, parsedCharacteristics, page ?? 1, pageSize ?? 20);
        searchResult = await catalog.SearchAsync(searchRequest, ct);
    }
    catch (CatalogBadRequestException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    var offers = await pricing.GetOffersAsync(searchResult.Items.Select(p => p.ProductId).ToList(), ct);
    var offersByProduct = offers.Offers.ToDictionary(o => o.ProductId);

    IEnumerable<ProductCandidateDto> candidates = searchResult.Items.Select(p => ToCandidate(p, offersByProduct));

    if (priceMin is not null)
    {
        candidates = candidates.Where(c => c.PriceVerified && c.Price!.Amount >= priceMin);
    }

    if (priceMax is not null)
    {
        candidates = candidates.Where(c => c.PriceVerified && c.Price!.Amount <= priceMax);
    }

    candidates = sortBy switch
    {
        "price_asc" => candidates.Where(c => c.PriceVerified).OrderBy(c => c.Price!.Amount),
        "price_desc" => candidates.Where(c => c.PriceVerified).OrderByDescending(c => c.Price!.Amount),
        "name" => candidates.OrderBy(c => c.Name, StringComparer.Ordinal),
        _ => candidates,
    };

    return Results.Ok(candidates.ToList());
});

// POST /api/products/compare — thin proxy to Advisor's POST /api/comparisons (FR-018,
// contracts/gateway-bff-api.md): no reshaping, so this stays a single source of truth for the
// comparison response contract. The explicit picker's "Compare" button calls this directly.
app.MapPost("/api/products/compare", async (JsonElement request, AdvisorApiClient advisor, CancellationToken ct) =>
{
    var (statusCode, body) = await advisor.CompareAsync(request, ct);
    return Results.Json(body, statusCode: statusCode);
});

static List<CatalogCharacteristicFilterDto> ParseCharacteristics(string[]? raw)
{
    if (raw is null || raw.Length == 0)
    {
        return [];
    }

    var result = new List<CatalogCharacteristicFilterDto>();
    foreach (var entry in raw)
    {
        var parts = entry.Split(':', 4);
        if (parts.Length < 3)
        {
            throw new FormatException(
                $"Malformed characteristics entry '{entry}'; expected key:operator:value[:valueTo].");
        }

        result.Add(new CatalogCharacteristicFilterDto(parts[0], parts[1], parts[2], parts.Length == 4 ? parts[3] : null));
    }

    return result;
}

static ProductCandidateDto ToCandidate(CatalogProductDto product, Dictionary<Guid, PricingOfferDto> offersByProduct)
{
    offersByProduct.TryGetValue(product.ProductId, out var offer);
    return new ProductCandidateDto(
        product.ProductId, product.Name, product.Brand, product.Category, product.Specifications,
        Price: offer?.Price, PriceVerified: offer is not null,
        Availability: offer?.Availability, AvailabilityVerified: offer is not null);
}

app.Run();

public partial class Program;

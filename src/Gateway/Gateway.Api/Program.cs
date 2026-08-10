using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gateway.Api;
using Gateway.Api.Clients;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Polly;

var builder = WebApplication.CreateBuilder(args);

// FR-105: rejected before the body is even parsed — Kestrel itself returns 413 (via
// BadHttpRequestException, already handled by GlobalExceptionHandler) once this is exceeded.
// Mirrors ProductAdvisor.Api's own RequestGuardrails:MaxRequestBodyBytes default (Gateway has no
// dependency on ProductAdvisor.Application to share the strongly-typed options record with).
var maxRequestBodyBytes = builder.Configuration.GetValue("RequestGuardrails:MaxRequestBodyBytes", 64 * 1024L);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

builder.AddServiceDefaults();

// Every Gateway endpoint requires a Google-issued identity token, validated independently here
// against Google's own OIDC discovery document — Gateway never merely trusts that a call came
// from WebApp by network position (FR-030, research.md §17).
var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.MapInboundClaims = false;
        var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = !string.IsNullOrWhiteSpace(googleClientId),
            ValidAudience = googleClientId,
        };
    });

var authenticationSchemes = new List<string> { JwtBearerDefaults.AuthenticationScheme };

// EndToEnd.Tests/CI cannot perform a real interactive Google sign-in, so — only when this
// (non-production; never set in render.yaml, only in docker-compose.yml) configuration key is
// present — a second, entirely separate JwtBearer scheme accepts tokens signed with a
// test-only symmetric key instead. It shares nothing with the real Google scheme above, so it
// can never be used to forge a token the Google scheme would accept.
const string TestAuthenticationScheme = "E2ETest";
var testSigningKey = builder.Configuration["Authentication:TestSigningKey"];
if (!string.IsNullOrWhiteSpace(testSigningKey))
{
    authenticationBuilder.AddJwtBearer(TestAuthenticationScheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(testSigningKey)),
        };
    });
    authenticationSchemes.Add(TestAuthenticationScheme);
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(authenticationSchemes.ToArray())
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddTransient<UserIdForwardingHandler>();

var advisorBaseAddress = builder.Configuration.GetServiceBaseAddress("advisor-api");
var catalogBaseAddress = builder.Configuration.GetServiceBaseAddress("catalog-api");
var pricingBaseAddress = builder.Configuration.GetServiceBaseAddress("pricing-api");

// YARP also needs the public Render endpoints on the free plan. In local/Aspire and Docker
// environments these remain the logical service-discovery addresses from appsettings.json.
builder.Configuration["ReverseProxy:Clusters:catalog-cluster:Destinations:destination1:Address"] =
    catalogBaseAddress.ToString();
builder.Configuration["ReverseProxy:Clusters:pricing-cluster:Destinations:destination1:Address"] =
    pricingBaseAddress.ToString();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
builder.Services.AddHttpClient<AdvisorApiClient>(client => client.BaseAddress = advisorBaseAddress)
    .AddHttpMessageHandler<UserIdForwardingHandler>()
    // ServiceDefaults' standard resilience handler assumes short request/response calls (10s
    // per-attempt, 30s total, with retries) — wrong on two counts for the SSE streaming call:
    // a healthy in-progress stream can legitimately run past those windows, and retrying a
    // streaming POST would silently re-run a non-idempotent conversation turn. Replace it with
    // a single generous timeout and no retry for this client.
    .RemoveAllResilienceHandlers()
    .AddResilienceHandler("advisor-streaming", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(5)));
#pragma warning restore EXTEXP0001

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
builder.Services.AddHttpClient<CatalogApiClient>(client => client.BaseAddress = catalogBaseAddress)
    .RemoveAllResilienceHandlers()
    .AddResilienceHandler("catalog-cold-start", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(2)));
builder.Services.AddHttpClient<PricingApiClient>(client => client.BaseAddress = pricingBaseAddress)
    .RemoveAllResilienceHandlers()
    .AddResilienceHandler("pricing-cold-start", pipeline => pipeline.AddTimeout(TimeSpan.FromMinutes(2)));
#pragma warning restore EXTEXP0001

var app = builder.Build();

// Correlation id must wrap the exception handler, not the other way around — otherwise an
// unhandled exception's own log line is written after the correlation-id scope has already
// unwound and never carries it (FR-027, research.md §7/§16).
app.UseCorrelationId();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapReverseProxy();

// GET /api/system-status — aggregate startup-readiness check consumed by WebApp's starting-up
// screen (FR-033/FR-034/FR-035, research.md §19). Deliberately anonymous — see Authentication
// in contracts/gateway-bff-api.md — so it can run even before the Google sign-in redirect
// completes. Reuses each service's own /alive (FR-028); introduces no new health-check
// mechanism, only aggregates the one that already exists (research.md §13's pushdown-composition
// pattern, applied to liveness instead of product data).
app.MapGet("/api/system-status", async (
    CatalogApiClient catalog, PricingApiClient pricing, AdvisorApiClient advisor, CancellationToken ct) =>
{
    var checks = await Task.WhenAll(
        CheckServiceAsync("catalog-api", catalog.IsAliveAsync, ct),
        CheckServiceAsync("pricing-api", pricing.IsAliveAsync, ct),
        CheckServiceAsync("advisor-api", advisor.IsAliveAsync, ct));

    var overall = checks.All(c => c.Reachable) ? "ready" : "degraded";
    return Results.Ok(new SystemReadinessStatus(overall, checks));
})
.AllowAnonymous();

// A single service's liveness check must never make the whole aggregate hang or fail — each
// check gets its own short timeout, independent of the other two, and any failure (connection
// refused, timeout, broken circuit) is reported as simply "not reachable right now" rather than
// propagating as an error (constitution Principle V, FR-034).
static async Task<ServiceReadiness> CheckServiceAsync(
    string name, Func<CancellationToken, Task<bool>> isAliveAsync, CancellationToken ct)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    // A public request is what wakes a sleeping Render free instance. Render documents a cold
    // start of about one minute, so keep the probe alive long enough to both trigger and observe
    // that wake-up. The three probes run concurrently, not sequentially.
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

    bool reachable;
    try
    {
        reachable = await isAliveAsync(timeoutCts.Token);
    }
#pragma warning disable CA1031 // Intentional: any failure here means "not reachable," never an error the caller sees.
    catch (Exception)
#pragma warning restore CA1031
    {
        reachable = false;
    }

    return new ServiceReadiness(name, reachable, DateTimeOffset.UtcNow);
}

// POST /api/chat/messages — starts a session if none given, forwards the message, and merges
// the resolved sessionId into Advisor's (otherwise pass-through) response (contracts/gateway-bff-api.md).
app.MapPost("/api/chat/messages", async Task<IResult> (ChatMessageRequest request, AdvisorApiClient advisor, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Message text is required.");
    }

    var sessionId = request.SessionId ?? await advisor.CreateSessionAsync(ct);
    var (statusCode, turn) = await advisor.SendMessageAsync(sessionId, request.Text, ct);

    if ((int)statusCode >= 300)
    {
        // Relay Advisor's own controlled rejection verbatim (400 guardrail rejection, 404
        // unowned/unknown session, 409 in-flight-turn conflict) — never collapsed into a generic
        // 500 by assuming every response is a success.
        return Results.Json(turn, statusCode: (int)statusCode);
    }

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

// DELETE /api/chat/{sessionId} — pass-through of user-initiated single-session deletion (FR-119).
app.MapDelete("/api/chat/{sessionId:guid}", async Task<IResult> (Guid sessionId, AdvisorApiClient advisor, CancellationToken ct) =>
{
    var statusCode = await advisor.DeleteSessionAsync(sessionId, ct);
    return statusCode switch
    {
        HttpStatusCode.NoContent => Results.NoContent(),
        HttpStatusCode.Conflict => Results.Conflict("A turn for this session is already being processed."),
        _ => Results.NotFound(),
    };
});

// DELETE /api/chat — pass-through of user-initiated deletion of every one of the caller's
// sessions (FR-119).
app.MapDelete("/api/chat", async (AdvisorApiClient advisor, CancellationToken ct) =>
{
    await advisor.DeleteAllSessionsAsync(ct);
    return Results.NoContent();
});

// GET /api/products/{productId} — plain product-detail composition (US3, outside the chat
// flow, e.g. a "view product" link from a recommendation): concurrent Catalog+Pricing fetch,
// 404 only if Catalog has no such product; a Pricing outage degrades to unverified price/
// availability rather than failing the whole request (constitution Principle V).
app.MapGet("/api/products/{productId:guid}", async Task<IResult> (
    Guid productId, CatalogApiClient catalog, PricingApiClient pricing, CancellationToken ct) =>
{
    var detailTask = catalog.GetProductDetailAsync(productId, ct);
    var offerTask = pricing.GetOfferAsync(productId, ct);
    await Task.WhenAll(detailTask, offerTask);

    var detail = await detailTask;
    if (detail is null)
    {
        return Results.NotFound();
    }

    var offer = await offerTask;
    return Results.Ok(new ProductCandidateDto(
        detail.ProductId, detail.Name, detail.Brand, detail.Category, detail.Specifications,
        Price: offer?.Price, PriceVerified: offer is not null,
        Availability: offer?.Availability, AvailabilityVerified: offer is not null));
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

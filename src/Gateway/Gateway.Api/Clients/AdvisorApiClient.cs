using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Gateway.Api.Clients;

/// <summary>
/// Thin client to the Advisor's conversation HTTP API. Deliberately passes response bodies
/// through as raw JSON rather than re-typing them — the Gateway composes/forwards, it doesn't
/// reshape the Advisor's response contract (contracts/gateway-bff-api.md).
/// </summary>
public sealed class AdvisorApiClient(HttpClient httpClient)
{
    public async Task<Guid> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync("/api/conversations", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("sessionId").GetGuid();
    }

    /// <summary>
    /// Deliberately does NOT call <c>EnsureSuccessStatusCode()</c> — a controlled rejection from
    /// Advisor (a `400` guardrail rejection, FR-113; a `409` in-flight-turn conflict, FR-024) is
    /// a real, meaningful response the caller must relay verbatim, not an exception to collapse
    /// into a generic `500` (a real gap this fixes: it previously did call
    /// EnsureSuccessStatusCode, silently turning every Advisor-side `4xx` into an
    /// undifferentiated Gateway `500` — found by running the Phase 14 eval suite against the
    /// live stack, which is exactly the kind of defect that smoke test exists to catch).
    /// </summary>
    public async Task<(HttpStatusCode StatusCode, JsonElement Body)> SendMessageAsync(Guid sessionId, string text, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new { text }, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return (response.StatusCode, body);
    }

    /// <summary>
    /// Streaming sibling of <see cref="SendMessageAsync"/> (contracts/gateway-bff-api.md) —
    /// relays the Advisor's <c>token</c>/<c>result</c> SSE events almost verbatim; the caller
    /// merges its own <c>sessionId</c> into the <c>result</c> event.
    /// </summary>
    public async IAsyncEnumerable<(string EventType, string Data)> StreamMessageAsync(
        Guid sessionId, string text, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{sessionId}/messages/stream")
        {
            Content = JsonContent.Create(new { text }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
        {
            yield return (item.EventType, item.Data);
        }
    }

    public async Task<JsonElement?> GetSnapshotAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/conversations/{sessionId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    /// <summary>
    /// Thin proxy to <c>POST /api/comparisons</c> (contracts/gateway-bff-api.md) — forwards the
    /// request body and relays whatever status/body Advisor returned verbatim, so this stays a
    /// single source of truth for the comparison response contract rather than a second one.
    /// </summary>
    public async Task<(int StatusCode, JsonElement Body)> CompareAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/api/comparisons", request, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return ((int)response.StatusCode, body);
    }

    /// <summary>Used only by <c>GET /api/system-status</c> (FR-033, research.md §19) — reuses the
    /// service's own liveness check rather than a second "are you up" mechanism.</summary>
    public async Task<bool> IsAliveAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync("/alive", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>FR-119: user-initiated deletion of one session. Returns whether the session was
    /// found and owned by the caller (mirroring the 404 <see cref="GetSnapshotAsync"/> already
    /// gives for the same two indistinguishable cases) or whether it's still mid-turn (409).</summary>
    public async Task<HttpStatusCode> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var response = await httpClient.DeleteAsync($"/api/conversations/{sessionId}", cancellationToken);
        return response.StatusCode;
    }

    /// <summary>FR-119: user-initiated deletion of every session belonging to the caller.</summary>
    public async Task DeleteAllSessionsAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.DeleteAsync("/api/conversations", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

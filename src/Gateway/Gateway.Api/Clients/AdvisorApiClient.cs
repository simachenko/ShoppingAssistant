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

    public async Task<JsonElement> SendMessageAsync(Guid sessionId, string text, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new { text }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
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
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WebApp.Blazor.Models;

namespace WebApp.Blazor.Services;

public sealed class GatewayApiClient(HttpClient httpClient)
{
    // The Gateway's SSE `result` event is hand-serialized (not via Results.Ok(...)'s Web JSON
    // defaults), so it must be read back with the same casing policy or fields silently bind to
    // default(T) instead of throwing.
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatTurnDto> SendMessageAsync(Guid? sessionId, string text, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/chat/messages", new { sessionId, text }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChatTurnDto>(cancellationToken);
        return result ?? throw new InvalidOperationException("Gateway returned an empty chat response.");
    }

    /// <summary>
    /// Streaming sibling of <see cref="SendMessageAsync"/> (FR-015/research.md §11) — reads the
    /// Gateway's SSE response incrementally via .NET's built-in <see cref="SseParser"/>, yielding
    /// narration deltas as they arrive and finally the complete <see cref="ChatTurnDto"/>.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> StreamMessageAsync(
        Guid? sessionId, string text, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/messages/stream")
        {
            Content = JsonContent.Create(new { sessionId, text }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
        {
            if (item.EventType == "token")
            {
                using var doc = JsonDocument.Parse(item.Data);
                yield return new ChatStreamEvent(doc.RootElement.GetProperty("delta").GetString(), null);
            }
            else if (item.EventType == "result")
            {
                var result = JsonSerializer.Deserialize<ChatTurnDto>(item.Data, ResultJsonOptions)
                    ?? throw new InvalidOperationException("Gateway returned an empty streamed result.");
                yield return new ChatStreamEvent(null, result);
            }
        }
    }

    /// <summary>
    /// Explicit product-picker search (FR-020, contracts/gateway-bff-api.md) — no chat, no
    /// sessionId, no LLM involvement at all.
    /// </summary>
    public async Task<IReadOnlyList<ProductCandidateDto>> SearchProductsAsync(
        string? category, string? query, decimal? priceMin, decimal? priceMax, string? sortBy,
        CancellationToken cancellationToken)
    {
        var queryString = new List<string>();
        if (!string.IsNullOrWhiteSpace(category)) queryString.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(query)) queryString.Add($"q={Uri.EscapeDataString(query)}");
        if (priceMin is not null) queryString.Add($"priceMin={priceMin}");
        if (priceMax is not null) queryString.Add($"priceMax={priceMax}");
        if (!string.IsNullOrWhiteSpace(sortBy)) queryString.Add($"sortBy={Uri.EscapeDataString(sortBy)}");

        var uri = "/api/products/search" + (queryString.Count > 0 ? "?" + string.Join('&', queryString) : "");
        var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyList<ProductCandidateDto>>(cancellationToken);
        return result ?? [];
    }

    /// <summary>
    /// Explicit product-picker comparison (FR-018, contracts/gateway-bff-api.md) — a thin proxy
    /// through the Gateway to Advisor's stateless <c>POST /api/comparisons</c>; byte-identical to
    /// the same ids compared via chat (SC-010).
    /// </summary>
    public async Task<ComparisonResultDto> CompareAsync(
        IReadOnlyList<Guid> productIds, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/products/compare", new { productIds, includeExplanation = true }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ComparisonResultDto>(cancellationToken);
        return result ?? throw new InvalidOperationException("Gateway returned an empty comparison response.");
    }
}

using System.Net;
using System.Net.Http.Json;
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
}

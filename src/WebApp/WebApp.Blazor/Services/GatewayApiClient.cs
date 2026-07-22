using System.Net.Http.Json;
using WebApp.Blazor.Models;

namespace WebApp.Blazor.Services;

public sealed class GatewayApiClient(HttpClient httpClient)
{
    public async Task<ChatTurnDto> SendMessageAsync(Guid? sessionId, string text, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/chat/messages", new { sessionId, text }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChatTurnDto>(cancellationToken);
        return result ?? throw new InvalidOperationException("Gateway returned an empty chat response.");
    }
}

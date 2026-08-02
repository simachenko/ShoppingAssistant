using System.Net.Http.Json;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises constitution Principle VI (research.md §7): one correlation id, generated or
/// echoed by the Gateway, must flow through the entire real service chain — Gateway → Advisor →
/// Catalog/Pricing — appearing in each service's own structured logs, not just in HTTP headers.
/// </summary>
public sealed class ObservabilityTests : IClassFixture<DockerComposeStackFixture>, IDisposable
{
    private readonly HttpClient _client;

    public ObservabilityTests(DockerComposeStackFixture fixture)
    {
        _ = fixture;
        _client = DockerComposeStackFixture.CreateAuthenticatedGatewayClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task One_correlation_id_is_echoed_by_the_gateway_and_appears_in_every_downstream_services_logs()
    {
        var correlationId = $"e2e-observability-{Guid.NewGuid():n}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/messages")
        {
            Content = JsonContent.Create(new
            {
                sessionId = (Guid?)null,
                text = "I need a smartphone with a good camera and a budget of up to 15000 UAH",
            }),
        };
        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var echoed));
        Assert.Equal(correlationId, echoed!.Single());

        // Log lines are written asynchronously to the container's stdout; give them a moment to land.
        await Task.Delay(TimeSpan.FromSeconds(2));

        foreach (var service in new[] { "gateway-api", "advisor-api", "catalog-api", "pricing-api" })
        {
            var logs = await DockerComposeCli.LogsAsync(service);
            Assert.True(
                logs.Contains($"CorrelationId:{correlationId}", StringComparison.Ordinal),
                $"Expected '{service}' logs to contain the request's correlation id, but they didn't.\n" +
                "Last 2000 chars of that service's log tail:\n" + Tail(logs, 2000));
        }
    }

    private static string Tail(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[^maxChars..];
}

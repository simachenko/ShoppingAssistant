using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises quickstart.md Scenario 6 (constitution Principle V) against the real
/// docker-compose stack: with the Pricing service stopped, a recommendation request must still
/// return <c>200</c> with an honest <c>priceVerified: false</c>/<c>availabilityVerified: false</c>
/// per item — never a <c>5xx</c>, a stale price, or a fabricated one. The Pricing container is
/// always restarted afterward (in a <c>finally</c> block) so the shared stack is left healthy for
/// any other test or session using it.
/// </summary>
public sealed class PartialFailureResilienceTests : IClassFixture<DockerComposeStackFixture>, IDisposable
{
    private readonly HttpClient _client;

    public PartialFailureResilienceTests(DockerComposeStackFixture fixture)
    {
        _ = fixture;
        _client = DockerComposeStackFixture.CreateAuthenticatedGatewayClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Scenario_6_a_pricing_outage_yields_an_honest_partial_recommendation_not_a_crash()
    {
        await DockerComposeCli.RunAsync("stop pricing-api");
        try
        {
            // Give the stopped container's connections time to actually drop rather than racing
            // a request against a container that's still mid-shutdown.
            await Task.Delay(TimeSpan.FromSeconds(2));

            var response = await _client.PostAsJsonAsync("/api/chat/messages", new
            {
                sessionId = (Guid?)null,
                text = "I need a smartphone with a good camera and a budget of up to 15000 UAH",
            });

            Assert.True(
                response.IsSuccessStatusCode,
                $"Expected a successful response even with Pricing down, got {(int)response.StatusCode}.");

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("recommendation", body.GetProperty("type").GetString());

            // Hard constraints (category, required features) are still evaluable from Catalog
            // alone, so candidates should still surface — just honestly unverified on price/stock.
            var items = body.GetProperty("items").EnumerateArray().ToList();
            Assert.NotEmpty(items);
            Assert.All(items, item =>
            {
                Assert.False(item.GetProperty("priceVerified").GetBoolean());
                Assert.False(item.GetProperty("availabilityVerified").GetBoolean());
            });
        }
        finally
        {
            await DockerComposeCli.RunAsync("start pricing-api");
            await WaitUntilPricingIsHealthyAsync();
        }
    }

    private static async Task WaitUntilPricingIsHealthyAsync()
    {
        using var pricingClient = new HttpClient { BaseAddress = new Uri("http://localhost:5102") };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var response = await pricingClient.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Still coming up — retry.
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException("pricing-api did not become healthy again after restart.");
    }
}

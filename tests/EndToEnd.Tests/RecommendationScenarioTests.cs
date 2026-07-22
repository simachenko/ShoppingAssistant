using System.Net.Http.Json;
using System.Text.Json;
using TestSupport.SeedData;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises quickstart.md Scenarios 1–3 against the real docker-compose stack (Gateway →
/// Advisor → real LLM → Catalog/Pricing). Requires the stack to be running
/// (<c>docker compose up --build</c>) AND a real LLM_PROVIDER_* configured for the Advisor
/// service (see docker-compose.yml) — this is the one test suite that genuinely needs a live
/// LLM, because natural-language understanding is deliberately the LLM's job, not deterministic
/// code (constitution/plan.md Summary).
/// </summary>
public sealed class RecommendationScenarioTests : IClassFixture<DockerComposeStackFixture>, IDisposable
{
    private readonly HttpClient _client;

    public RecommendationScenarioTests(DockerComposeStackFixture fixture)
    {
        _ = fixture; // ensures the stack has been seeded before any test in this class runs
        _client = new HttpClient { BaseAddress = new Uri(DockerComposeStackFixture.GatewayBaseUrl) };
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Scenario_1_fully_specified_request_yields_a_grounded_recommendation()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "I need a smartphone with a good camera and a budget of up to 15000 UAH",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("recommendation", body.GetProperty("type").GetString());
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0);

        foreach (var item in items.EnumerateArray())
        {
            var price = item.GetProperty("price");
            Assert.True(price.GetProperty("amount").GetDecimal() <= 15000m);
            Assert.True(item.GetProperty("matchedRequirements").GetArrayLength() > 0);
            Assert.True(item.GetProperty("tradeOffs").GetArrayLength() > 0);
        }

        // Galaxy S24 (14500 UAH, 50MP camera) should be among the recommended candidates.
        Assert.Contains(
            items.EnumerateArray(),
            i => i.GetProperty("productId").GetGuid() == CatalogSeedData.GalaxyS24Id);
    }

    [Fact]
    public async Task Scenario_2_missing_budget_yields_exactly_one_clarifying_question()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "I need a good laptop",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("clarification", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("question").GetString()));
    }

    [Fact]
    public async Task Scenario_3_budget_below_every_product_yields_an_honest_no_match()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "I need a smartphone for under 500 UAH",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("recommendation", body.GetProperty("type").GetString());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("unmetConstraintExplanation").GetString()));
    }
}

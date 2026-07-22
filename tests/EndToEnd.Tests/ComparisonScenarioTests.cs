using System.Net.Http.Json;
using System.Text.Json;
using TestSupport.SeedData;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises quickstart.md Scenario 4 (compare products with consistent criteria and
/// deterministic rating/delta) against the real docker-compose stack — see
/// <see cref="RecommendationScenarioTests"/> for why this needs a live LLM.
/// </summary>
public sealed class ComparisonScenarioTests : IClassFixture<DockerComposeStackFixture>, IDisposable
{
    private readonly HttpClient _client;

    public ComparisonScenarioTests(DockerComposeStackFixture fixture)
    {
        _ = fixture;
        _client = new HttpClient { BaseAddress = new Uri(DockerComposeStackFixture.GatewayBaseUrl) };
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Scenario_4_comparing_two_named_products_yields_identical_criteria_and_deterministic_rating_delta()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "Compare the Galaxy S24 and the Pixel 9",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("comparison", body.GetProperty("type").GetString());

        var criteria = body.GetProperty("criteria").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Contains("camera_mp", criteria);
        Assert.Contains("battery_mah", criteria);
        Assert.Contains("storage_gb", criteria);
        Assert.Contains("price", criteria);
        Assert.Contains("availability", criteria);

        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);

        // FR-006/SC-002: every row must expose the identical criteria set, same order.
        foreach (var row in rows)
        {
            var rowCriteria = row.GetProperty("values").EnumerateObject().Select(p => p.Name).ToList();
            Assert.Equal(criteria, rowCriteria);
        }

        var galaxyRow = rows.Single(r => r.GetProperty("productId").GetGuid() == CatalogSeedData.GalaxyS24Id);
        var pixelRow = rows.Single(r => r.GetProperty("productId").GetGuid() == CatalogSeedData.Pixel9Id);

        // Deterministic facts from PricingSeedData/CatalogSeedData: Galaxy S24 is cheaper
        // (14500 vs 15800 UAH) and has less battery (4000 vs 4700 mAh) than the Pixel 9.
        Assert.Equal("50", galaxyRow.GetProperty("values").GetProperty("camera_mp").GetString());
        Assert.Equal("50", pixelRow.GetProperty("values").GetProperty("camera_mp").GetString());
        Assert.Contains("cheapest", galaxyRow.GetProperty("deltasVsBest").GetProperty("price").GetString());
        Assert.Contains("vs cheapest", pixelRow.GetProperty("deltasVsBest").GetProperty("price").GetString());
        Assert.Equal("best in set", pixelRow.GetProperty("deltasVsBest").GetProperty("battery_mah").GetString());

        // Repeating the exact same request must yield byte-identical rating/delta values —
        // proving the computation is deterministic, not dependent on the LLM call in between.
        var secondResponse = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "Compare the Galaxy S24 and the Pixel 9",
        });
        secondResponse.EnsureSuccessStatusCode();
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondGalaxyRow = secondBody.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("productId").GetGuid() == CatalogSeedData.GalaxyS24Id);

        Assert.Equal(galaxyRow.GetProperty("rating").GetDecimal(), secondGalaxyRow.GetProperty("rating").GetDecimal());
    }
}

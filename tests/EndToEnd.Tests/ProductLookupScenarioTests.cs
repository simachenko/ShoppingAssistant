using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises quickstart.md Scenario 5 against the real docker-compose stack (Gateway → Advisor →
/// real LLM → Catalog/Pricing) — a verified single-product fact lookup, and an honest "not
/// found" for a product that doesn't exist (US3, FR-005, FR-012). Like
/// <see cref="RecommendationScenarioTests"/>, this genuinely needs a live LLM: understanding
/// "is X in stock and what does it cost" as a fact-lookup request is the LLM's job, not
/// deterministic code.
/// </summary>
public sealed class ProductLookupScenarioTests : IClassFixture<DockerComposeStackFixture>, IDisposable
{
    private readonly HttpClient _client;

    public ProductLookupScenarioTests(DockerComposeStackFixture fixture)
    {
        _ = fixture; // ensures the stack has been seeded before any test in this class runs
        _client = DockerComposeStackFixture.CreateAuthenticatedGatewayClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Scenario_5a_a_verified_fact_lookup_states_the_seeded_price_and_availability()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "Is the Samsung Galaxy S24 in stock and what does it cost?",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var narration = body.GetProperty("question").GetString() ?? body.GetProperty("message").GetString() ?? "";
        Assert.Contains("14500", narration.Replace(",", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("stock", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scenario_5b_a_nonexistent_product_gets_an_honest_not_found_never_an_invented_price()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "What's the price of the Totally Fake Product 9000?",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var narration = body.GetProperty("question").GetString() ?? body.GetProperty("message").GetString() ?? "";
        Assert.Matches("(?i)(not (be )?found|couldn't find|could not find|does not exist|doesn't exist|no such product|unable to find)", narration);
    }
}

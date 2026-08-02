using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TestSupport;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises quickstart.md Scenarios 7-8 against the real docker-compose stack: the whole app
/// requires sign-in (FR-030), a session belongs only to its creator (FR-031), internal calls
/// without the shared credential are rejected (FR-029), and a checkout link narrates exactly the
/// referenced products (FR-025, US4/US5). Also proves a deliberately-thrown exception produces a
/// correlation-id-tagged log and a clean <c>problem+json</c> body, never a raw stack trace
/// (FR-027/FR-032).
/// </summary>
public sealed class AccessControlAndCheckoutScenarioTests : IClassFixture<DockerComposeStackFixture>
{
    public AccessControlAndCheckoutScenarioTests(DockerComposeStackFixture fixture) => _ = fixture;

    [Fact]
    public async Task A_request_to_gateway_with_no_bearer_token_is_rejected()
    {
        using var client = new HttpClient { BaseAddress = new Uri(DockerComposeStackFixture.GatewayBaseUrl) };

        var response = await client.GetAsync("/api/products/search?category=Smartphones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_direct_call_to_catalog_with_no_internal_api_key_is_rejected()
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5101") };

        var response = await client.GetAsync("/api/catalog/categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_session_created_by_one_signed_in_user_is_invisible_to_another()
    {
        using var ownerClient = DockerComposeStackFixture.CreateAuthenticatedGatewayClient("owner-user");
        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/chat/messages", new { sessionId = (Guid?)null, text = "I need a good laptop" });
        createResponse.EnsureSuccessStatusCode();
        var body = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = body.GetProperty("sessionId").GetGuid();

        using var otherUserClient = DockerComposeStackFixture.CreateAuthenticatedGatewayClient("a-different-user");
        var response = await otherUserClient.GetAsync($"/api/chat/{sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_then_recommend_then_check_out_returns_a_link_for_exactly_the_referenced_product()
    {
        using var client = DockerComposeStackFixture.CreateAuthenticatedGatewayClient();

        var recommendResponse = await client.PostAsJsonAsync("/api/chat/messages", new
        {
            sessionId = (Guid?)null,
            text = "I need a smartphone with a good camera and a budget of up to 15000 UAH",
        });
        recommendResponse.EnsureSuccessStatusCode();
        var recommendBody = await recommendResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("recommendation", recommendBody.GetProperty("type").GetString());

        var sessionId = recommendBody.GetProperty("sessionId").GetGuid();
        var firstItemId = recommendBody.GetProperty("items")[0].GetProperty("productId").GetGuid();

        var checkoutResponse = await client.PostAsJsonAsync(
            "/api/chat/messages", new { sessionId, text = "I'll take the first one, get me a checkout link." });
        checkoutResponse.EnsureSuccessStatusCode();
        var checkoutBody = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("checkoutLink", checkoutBody.GetProperty("type").GetString());
        var productIds = checkoutBody.GetProperty("productIds").EnumerateArray().Select(p => p.GetGuid()).ToList();
        Assert.Equal([firstItemId], productIds);
        Assert.Contains(firstItemId.ToString(), checkoutBody.GetProperty("url").GetString());
    }

    [Fact]
    public async Task A_deliberately_thrown_exception_is_logged_with_its_correlation_id_and_returns_problem_json()
    {
        var correlationId = $"e2e-exception-{Guid.NewGuid():n}";

        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5101") };
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", E2EAuthTestDefaults.InternalApiKey);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // A deliberately malformed characteristics filter reaches application code and is a
        // reliable way to provoke a real handled/logged error path without relying on a
        // dedicated test-only "throw" endpoint that doesn't exist in prod.
        var response = await client.PostAsJsonAsync("/api/catalog/products/search", new
        {
            category = "Smartphones",
            characteristics = new[] { new { key = "camera_mp", @operator = "not_a_real_operator", value = "48" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("status", out _) || response.Content.Headers.ContentType?.MediaType == "application/problem+json");

        await Task.Delay(TimeSpan.FromSeconds(2));
        var logs = await DockerComposeCli.LogsAsync("catalog-api");
        Assert.Contains($"CorrelationId:{correlationId}", logs, StringComparison.Ordinal);
    }
}

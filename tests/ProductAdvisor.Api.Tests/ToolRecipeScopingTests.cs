using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 10 (spec.md FR-066–FR-070, data-model.md `ToolRecipe`): the tool-list surface offered
/// to the language model for a turn is limited to exactly that route's fixed recipe, never the
/// full seven-tool catalog. NOT run in this sandbox — like the rest of this test project, it
/// requires a Testcontainers Postgres instance (Docker), unavailable here; verified by
/// inspection and by the unit-level coverage in
/// <c>ProductAdvisor.Application.Tests.TurnResourceBudgetTests</c>/<c>TurnResultTypeTests</c>
/// instead.
/// </summary>
public sealed class ToolRecipeScopingTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    private static readonly HashSet<string> ProductFactRecipe = ["search_products", "get_product_details", "check_price_and_availability"];

    [Fact]
    public async Task A_recommend_route_turns_narration_call_offers_no_tools_at_all()
    {
        // recommend is fully deterministic (Phase 9, IRecommendationService) — its narration call
        // never offers any tool, let alone compare_products/generate_checkout_link.
        var chatClient = new ExtractionAwareScriptedChatClient(
            """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":15000,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""",
            "Nothing quite fits right now.");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            ChatClientOverride = chatClient,
            // The route really runs its deterministic compute step, so Catalog and Pricing must
            // answer. The default responders 404, which failed the turn before it could reach the
            // narration call this test is actually about. Empty result sets are enough — the
            // assertion is about which tools were offered, not about what was found.
            CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse([], 0, 50, 0)),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse([], [])),
        };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("I need a smartphone under 15000 UAH"));

        response.EnsureSuccessStatusCode();
        Assert.All(chatClient.OfferedToolNamesPerCall, offered => Assert.Empty(offered));
    }

    [Fact]
    public async Task An_unsupported_route_makes_no_second_call_and_therefore_offers_no_tools()
    {
        var chatClient = new ExtractionAwareScriptedChatClient(
            """{"intent":"unsupported","productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""",
            "unused");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("write me a poem"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.Equal("unsupported", body!.Type);
        Assert.Single(chatClient.OfferedToolNamesPerCall); // extraction only.
    }

    [Fact]
    public async Task A_product_fact_route_turn_only_offers_its_recipes_read_only_tools()
    {
        var chatClient = new ExtractionAwareScriptedChatClient(
            """{"intent":"product_fact","productReferences":["Nokia 3310 Pro"],"missingFields":[],"confidence":0.9,"language":"en"}""",
            "I couldn't find that product in our catalog.");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("Is the Nokia 3310 Pro in stock?"));

        response.EnsureSuccessStatusCode();
        var routeCallOfferedTools = chatClient.OfferedToolNamesPerCall[1];
        Assert.True(routeCallOfferedTools.ToHashSet().IsSubsetOf(ProductFactRecipe));
        Assert.DoesNotContain("compare_products", routeCallOfferedTools);
        Assert.DoesNotContain("generate_checkout_link", routeCallOfferedTools);
        Assert.DoesNotContain("get_recommendations", routeCallOfferedTools);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }
}

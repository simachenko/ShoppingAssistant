using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class ConversationApiContractTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    private static readonly string[] RequiredCameraFeature = ["camera_mp"];

    [Fact]
    public async Task Clarification_response_is_returned_when_the_LLM_asks_instead_of_calling_a_tool()
    {
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            ChatClientOverride = new ScriptedChatClient(null, null, "What's your budget for this laptop?"),
        };
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("I need a good laptop"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.NotNull(body);
        Assert.Equal("clarification", body!.Type);
        Assert.Equal("What's your budget for this laptop?", body.Question);
        Assert.Null(body.Items);
    }

    [Fact]
    public async Task Recommendation_response_reflects_the_get_recommendations_tool_output_verbatim()
    {
        var productId = Guid.NewGuid();

        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse(
                [
                    new CatalogProductDto(productId, "Galaxy S24", "Samsung", "Smartphones", Guid.NewGuid(),
                        [new CatalogSpecificationDto("camera_mp", "50", "MP")]),
                ], 1, 50, 1)),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [
                    new PricingOfferDto(productId, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                ], [])),
            ChatClientOverride = new ScriptedChatClient(
                "get_recommendations",
                new Dictionary<string, object?>
                {
                    ["category"] = "Smartphones",
                    ["budgetAmount"] = 15000m,
                    ["budgetCurrency"] = "UAH",
                    ["requiredFeatures"] = RequiredCameraFeature,
                },
                "Here's a smartphone within your budget with a great camera."),
        };
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("I need a smartphone with a good camera and a budget of up to 15000 UAH"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.NotNull(body);
        Assert.Equal("recommendation", body!.Type);
        Assert.NotNull(body.Items);
        var item = Assert.Single(body.Items!);
        Assert.Equal("Galaxy S24", item.Name);
        Assert.Equal(14500m, item.Price!.Amount);
        Assert.Null(body.UnmetConstraintExplanation);
    }

    [Fact]
    public async Task Comparison_response_reflects_the_compare_products_tool_output_verbatim()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == $"/api/catalog/products/{productA}")
                {
                    return (HttpStatusCode.OK, new CatalogProductDto(productA, "Galaxy S24", "Samsung", "Smartphones",
                        categoryId, [new CatalogSpecificationDto("camera_mp", "50", "MP")]));
                }

                if (path == $"/api/catalog/products/{productB}")
                {
                    return (HttpStatusCode.OK, new CatalogProductDto(productB, "Pixel 9", "Google", "Smartphones",
                        categoryId, [new CatalogSpecificationDto("camera_mp", "48", "MP")]));
                }

                if (path == $"/api/catalog/categories/{categoryId}")
                {
                    return (HttpStatusCode.OK, new CatalogCategoryDto(categoryId, "Smartphones", ["camera_mp"]));
                }

                return (HttpStatusCode.NotFound, null);
            },
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [
                    new PricingOfferDto(productA, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                    new PricingOfferDto(productB, new PricingMoneyDto(13500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                ], [])),
            ChatClientOverride = new ScriptedChatClient(
                "compare_products",
                new Dictionary<string, object?> { ["productIds"] = new[] { productA.ToString(), productB.ToString() } },
                "Here's how the Galaxy S24 and Pixel 9 compare."),
        };
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("Compare the Galaxy S24 and the Pixel 9"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.NotNull(body);
        Assert.Equal("comparison", body!.Type);
        Assert.Null(body.Items);
        Assert.NotNull(body.Criteria);
        Assert.Equal(["price", "camera_mp", "availability"], body.Criteria);
        Assert.NotNull(body.Rows);
        Assert.Equal(2, body.Rows!.Count);
        Assert.Contains(body.Rows, r => r.Name == "Galaxy S24" && r.Values["camera_mp"] == "50");
        Assert.Contains(body.Rows, r => r.Name == "Pixel 9" && r.Values["camera_mp"] == "48");
    }

    [Fact]
    public async Task Requirement_persists_across_turns_and_is_visible_on_the_snapshot_endpoint()
    {
        var productId = Guid.NewGuid();

        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse(
                [new CatalogProductDto(productId, "XPS 13", "Dell", "Laptops", Guid.NewGuid(), [])], 1, 50, 1)),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [new PricingOfferDto(productId, new PricingMoneyDto(25000m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed")], [])),
            ChatClientOverride = new ScriptedChatClient(
                "get_recommendations",
                new Dictionary<string, object?> { ["category"] = "Laptops", ["budgetAmount"] = 30000m, ["budgetCurrency"] = "UAH" },
                "Here's a laptop within your budget."),
        };
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client);

        await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("I need a laptop for up to 30000 UAH"));

        var snapshotResponse = await client.GetAsync($"/api/conversations/{sessionId}");
        snapshotResponse.EnsureSuccessStatusCode();
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<ConversationSnapshotResponse>();

        Assert.NotNull(snapshot);
        Assert.Equal("Laptops", snapshot!.CurrentRequirement.Category);
        Assert.Equal(30000m, snapshot.CurrentRequirement.Budget!.Amount);
        Assert.Equal(2, snapshot.Messages.Count); // user + assistant
    }

    [Fact]
    public async Task Unknown_session_returns_404()
    {
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/conversations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }
}

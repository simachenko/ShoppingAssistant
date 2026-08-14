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

    /// <summary>
    /// Renamed from "…when_the_LLM_asks_instead_of_calling_a_tool": under the deterministic turn
    /// cycle the model never authors a clarification. Policy routing decides an essential field is
    /// missing (FR-002/FR-041) and the question is built in code, so what this now guards is that
    /// the scripted narration is *not* what reaches the shopper.
    /// </summary>
    [Fact]
    public async Task A_turn_missing_an_essential_field_returns_the_cycles_own_clarification_not_the_models_text()
    {
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            // Routes to Clarify because the essential budget is absent (FR-002). The question
            // itself is built deterministically by the cycle, not taken from the model, so the
            // scripted narration is deliberately never what the shopper sees.
            ChatClientOverride = new ScriptedChatClient(
                null, null, "unused narration",
                extractionJson: """{"intent":"recommend","requirementPatch":{"category":"Laptops"},"productReferences":[],"missingFields":["Budget"],"confidence":0.9,"language":"en"}"""),
        };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("I need a good laptop"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.NotNull(body);
        Assert.Equal("clarification", body!.Type);
        Assert.Equal("What's your budget for this?", body.Question);
        Assert.DoesNotContain("unused narration", body.Question!, StringComparison.Ordinal);
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
            // The `recommend` route invokes the recommendation service directly (FR-066) and
            // offers no tools at all, so there is nothing for the client to script here — the
            // requirement comes from the extraction patch instead.
            ChatClientOverride = new ScriptedChatClient(
                null, null,
                "Here's a smartphone within your budget with a great camera.",
                extractionJson: """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":15000,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}"""),
        };
        var client = factory.CreateAuthenticatedClient();
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
                "Here's how the Galaxy S24 and Pixel 9 compare.",
                extractionJson: """{"intent":"compare","productReferences":["Galaxy S24","Pixel 9"],"missingFields":[],"confidence":0.9,"language":"en"}"""),
        };
        var client = factory.CreateAuthenticatedClient();
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
                null, null, "Here's a laptop within your budget.",
                extractionJson: """{"intent":"recommend","requirementPatch":{"category":"Laptops","budgetAmount":30000,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}"""),
        };
        var client = factory.CreateAuthenticatedClient();
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
        var client = factory.CreateAuthenticatedClient();

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

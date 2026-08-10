using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 11's Independent Test (spec.md FR-088–FR-090): a narration claim absent from the
/// Evidence Envelope never reaches the client, while the structured `items`/`type` stay
/// byte-identical to the grounded case. NOT run in this sandbox — like the rest of this test
/// project, it requires a Testcontainers Postgres instance (Docker), unavailable here; verified
/// by inspection and by the runnable unit-level coverage in
/// <c>ProductAdvisor.Application.Tests.OutputValidationStageTests</c> instead.
/// </summary>
public sealed class NarrationGroundingTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    [Fact]
    public async Task A_fabricated_price_in_narration_never_reaches_the_client_while_items_stay_grounded()
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
            ChatClientOverride = new ExtractionAwareScriptedChatClient(
                """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":15000,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""",
                // A fabricated price nowhere in the Evidence Envelope (the real price is 14500).
                "This incredible phone is available for only 5 UAH, an unbeatable deal!"),
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
        Assert.DoesNotContain("5 UAH", body.Message);
        var item = Assert.Single(body.Items!);
        Assert.Equal("Galaxy S24", item.Name);
        Assert.Equal(14500m, item.Price!.Amount); // structured data stays correct regardless of narration's fate.
    }

    [Fact]
    public async Task A_grounded_narration_passes_through_unchanged()
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
            ChatClientOverride = new ExtractionAwareScriptedChatClient(
                """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":15000,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""",
                "The Galaxy S24 fits your 15000 UAH budget at 14500 UAH with a great 50 MP camera."),
        };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("I need a smartphone with a good camera and a budget of up to 15000 UAH"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.Equal("The Galaxy S24 fits your 15000 UAH budget at 14500 UAH with a great 50 MP camera.", body!.Message);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }
}

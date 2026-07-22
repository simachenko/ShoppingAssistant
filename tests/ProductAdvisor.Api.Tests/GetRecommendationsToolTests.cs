using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class GetRecommendationsToolTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AdvisorApiFactory _factory = new();

    [Fact]
    public async Task Get_recommendations_is_deterministic_across_repeated_calls_with_identical_input()
    {
        var productId = Guid.NewGuid();

        _factory.CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse(
            [
                new CatalogProductDto(productId, "Galaxy S24", "Samsung", "Smartphones",
                    [new CatalogSpecificationDto("camera_mp", "50", "MP")]),
            ], 1, 50, 1));

        _factory.PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
            [
                new PricingOfferDto(productId, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
            ], []));

        var httpClient = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient);
        await using var client = await McpClient.CreateAsync(transport);
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "get_recommendations");

        var args = new Dictionary<string, object?>
        {
            ["category"] = "Smartphones",
            ["budgetAmount"] = 15000m,
            ["budgetCurrency"] = "UAH",
            ["requiredFeatures"] = new[] { "camera_mp" },
        };

        var first = await tool.CallAsync(args);
        var second = await tool.CallAsync(args);

        Assert.NotEqual(true, first.IsError);
        Assert.NotEqual(true, second.IsError);

        var firstRecommendation = JsonSerializer.Deserialize<Recommendation>(first.StructuredContent!.Value, DeserializeOptions);
        var secondRecommendation = JsonSerializer.Deserialize<Recommendation>(second.StructuredContent!.Value, DeserializeOptions);

        Assert.NotNull(firstRecommendation);
        Assert.NotNull(secondRecommendation);
        Assert.Single(firstRecommendation!.Items);
        Assert.Equal(firstRecommendation.Items[0].Score, secondRecommendation!.Items[0].Score);
        Assert.Equal(firstRecommendation.Items[0].MatchedRequirements, secondRecommendation.Items[0].MatchedRequirements);
    }

    [Fact]
    public async Task Get_recommendations_returns_unmet_constraint_explanation_when_nothing_fits_budget()
    {
        var productId = Guid.NewGuid();

        _factory.CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse(
            [new CatalogProductDto(productId, "Flagship Phone", "Samsung", "Smartphones", [])], 1, 50, 1));

        _factory.PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
            [new PricingOfferDto(productId, new PricingMoneyDto(30000m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed")], []));

        var httpClient = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient);
        await using var client = await McpClient.CreateAsync(transport);
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "get_recommendations");

        var result = await tool.CallAsync(new Dictionary<string, object?>
        {
            ["category"] = "Smartphones",
            ["budgetAmount"] = 500m,
            ["budgetCurrency"] = "UAH",
        });

        var recommendation = JsonSerializer.Deserialize<Recommendation>(result.StructuredContent!.Value, DeserializeOptions);

        Assert.NotNull(recommendation);
        Assert.Empty(recommendation!.Items);
        Assert.NotNull(recommendation.UnmetConstraintExplanation);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

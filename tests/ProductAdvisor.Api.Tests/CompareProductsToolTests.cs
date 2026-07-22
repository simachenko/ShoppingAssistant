using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class CompareProductsToolTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AdvisorApiFactory _factory = new();

    private async Task<McpClient> CreateClientAsync()
    {
        var httpClient = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient);
        return await McpClient.CreateAsync(transport);
    }

    private void SetUpTwoSmartphones(Guid productA, Guid productB, Guid categoryId)
    {
        _factory.CatalogResponder = request =>
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
        };

        _factory.PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
            [
                new PricingOfferDto(productA, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                new PricingOfferDto(productB, new PricingMoneyDto(13500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
            ], []));
    }

    [Fact]
    public async Task Compare_products_uses_the_categorys_criteria_and_is_deterministic_across_repeated_calls()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        SetUpTwoSmartphones(productA, productB, categoryId);

        var client = await CreateClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "compare_products");
        var args = new Dictionary<string, object?> { ["productIds"] = new[] { productA.ToString(), productB.ToString() } };

        var first = await tool.CallAsync(args);
        var second = await tool.CallAsync(args);

        Assert.NotEqual(true, first.IsError);
        Assert.NotEqual(true, second.IsError);

        var firstComparison = JsonSerializer.Deserialize<Comparison>(first.StructuredContent!.Value, DeserializeOptions);
        var secondComparison = JsonSerializer.Deserialize<Comparison>(second.StructuredContent!.Value, DeserializeOptions);

        Assert.NotNull(firstComparison);
        Assert.NotNull(secondComparison);
        Assert.Equal(["price", "camera_mp", "availability"], firstComparison!.Criteria);
        Assert.Equal(2, firstComparison.Rows.Count);

        var firstRatings = firstComparison.Rows.Select(r => r.Rating).OrderBy(r => r);
        var secondRatings = secondComparison!.Rows.Select(r => r.Rating).OrderBy(r => r);
        Assert.Equal(firstRatings, secondRatings);
    }

    [Fact]
    public async Task Compare_products_with_fewer_than_two_ids_is_a_client_error()
    {
        var client = await CreateClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "compare_products");

        var result = await tool.CallAsync(new Dictionary<string, object?>
        {
            ["productIds"] = new[] { Guid.NewGuid().ToString() },
        });

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Compare_products_with_more_than_ten_ids_is_a_client_error()
    {
        var client = await CreateClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "compare_products");
        var tooMany = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid().ToString()).ToArray();

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["productIds"] = tooMany });

        Assert.True(result.IsError);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

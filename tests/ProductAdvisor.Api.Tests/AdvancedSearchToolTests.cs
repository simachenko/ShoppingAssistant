using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class AdvancedSearchToolTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new(JsonSerializerDefaults.Web);

    private readonly AdvisorApiFactory _factory = new();
    private McpClient? _client;

    private async Task<McpClient> GetClientAsync()
    {
        if (_client is not null)
        {
            return _client;
        }

        var httpClient = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient);

        _client = await McpClient.CreateAsync(transport);
        return _client;
    }

    [Fact]
    public async Task Tool_list_advertises_get_category()
    {
        var client = await GetClientAsync();

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "get_category");
    }

    [Fact]
    public async Task Get_category_resolves_by_name()
    {
        var categoryId = Guid.NewGuid();
        _factory.CatalogResponder = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/catalog/categories" &&
                request.RequestUri.Query.Contains("name=Smartphones", StringComparison.OrdinalIgnoreCase))
            {
                return (HttpStatusCode.OK, new CatalogCategoryDto(categoryId, "Smartphones", ["camera_mp", "battery_mah"]));
            }

            return (HttpStatusCode.NotFound, null);
        };
        var client = await GetClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "get_category");

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["name"] = "Smartphones" });

        Assert.NotEqual(true, result.IsError);
        Assert.True(result.StructuredContent!.Value.GetProperty("found").GetBoolean());
        Assert.Equal(
            categoryId,
            result.StructuredContent!.Value.GetProperty("category").GetProperty("categoryId").GetGuid());
    }

    [Fact]
    public async Task Get_category_returns_found_false_for_unknown_name()
    {
        _factory.CatalogResponder = _ => (HttpStatusCode.NotFound, null);
        var client = await GetClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "get_category");

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["name"] = "NoSuchCategory" });

        Assert.NotEqual(true, result.IsError);
        Assert.False(result.StructuredContent!.Value.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task Search_products_with_a_price_range_composes_pricing_and_filters_out_of_range_items()
    {
        var cheapId = Guid.NewGuid();
        var expensiveId = Guid.NewGuid();

        _factory.CatalogResponder = request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/catalog/products/search")
            {
                return (HttpStatusCode.OK, new CatalogSearchResponse(
                    [
                        new CatalogProductDto(cheapId, "Cheap Phone", "BrandA", "Smartphones", Guid.NewGuid(), []),
                        new CatalogProductDto(expensiveId, "Expensive Phone", "BrandB", "Smartphones", Guid.NewGuid(), []),
                    ], 1, 200, 2));
            }

            return (HttpStatusCode.NotFound, null);
        };
        _factory.PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
            [
                new PricingOfferDto(cheapId, new PricingMoneyDto(10000m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                new PricingOfferDto(expensiveId, new PricingMoneyDto(30000m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
            ], []));

        var client = await GetClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "search_products");

        var result = await tool.CallAsync(new Dictionary<string, object?>
        {
            ["category"] = "Smartphones",
            ["priceMax"] = 15000m,
        });

        Assert.NotEqual(true, result.IsError);
        var items = JsonSerializer.Deserialize<JsonElement[]>(result.StructuredContent!.Value, DeserializeOptions)!;
        var item = Assert.Single(items);
        Assert.Equal("Cheap Phone", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("priceVerified").GetBoolean());
    }

    [Fact]
    public async Task Search_products_without_price_or_sort_never_calls_pricing()
    {
        var productId = Guid.NewGuid();
        var pricingCalled = false;

        _factory.CatalogResponder = request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/catalog/products/search")
            {
                return (HttpStatusCode.OK, new CatalogSearchResponse(
                    [new CatalogProductDto(productId, "Galaxy S24", "Samsung", "Smartphones", Guid.NewGuid(), [])], 1, 200, 1));
            }

            return (HttpStatusCode.NotFound, null);
        };
        _factory.PricingResponder = _ =>
        {
            pricingCalled = true;
            return (HttpStatusCode.OK, new PricingBatchResponse([], []));
        };

        var client = await GetClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "search_products");

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["category"] = "Smartphones" });

        Assert.NotEqual(true, result.IsError);
        Assert.False(pricingCalled);
        var items = JsonSerializer.Deserialize<JsonElement[]>(result.StructuredContent!.Value, DeserializeOptions)!;
        var item = Assert.Single(items);
        Assert.False(item.GetProperty("priceVerified").GetBoolean());
    }

    [Fact]
    public async Task Search_products_never_requests_a_pageSize_that_would_exceed_catalogs_own_cap()
    {
        // Regression test: Catalog's parametric search endpoint rejects any pageSize over its own
        // MaxPageSize (100, ProductCatalogService.cs) with a 400. This fake responder enforces
        // that same cap, so a real search_products call that ever regresses to sending a bigger
        // default page size (as it once briefly did — 200) fails here exactly as it would against
        // the live Catalog service, instead of silently passing against a responder that ignores
        // pageSize altogether.
        const int catalogMaxPageSize = 100;
        var productId = Guid.NewGuid();

        _factory.CatalogResponder = request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/catalog/products/search")
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                var pageSize = doc.RootElement.GetProperty("pageSize").GetInt32();
                if (pageSize is < 1 or > catalogMaxPageSize)
                {
                    return (HttpStatusCode.BadRequest, null);
                }

                return (HttpStatusCode.OK, new CatalogSearchResponse(
                    [new CatalogProductDto(productId, "Galaxy S24", "Samsung", "Smartphones", Guid.NewGuid(), [])],
                    1, pageSize, 1));
            }

            return (HttpStatusCode.NotFound, null);
        };

        var client = await GetClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "search_products");

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["category"] = "Smartphones" });

        Assert.NotEqual(true, result.IsError);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }
}

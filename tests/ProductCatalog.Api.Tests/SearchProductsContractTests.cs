using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Contracts;
using Xunit;

namespace ProductCatalog.Api.Tests;

public sealed class SearchProductsContractTests(CatalogApiTestFixture fixture) : IClassFixture<CatalogApiTestFixture>
{
    [Fact]
    public async Task Search_by_category_returns_only_matching_seeded_products()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/products?category=Smartphones&page=1&pageSize=20");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        Assert.True(result!.TotalCount >= 3);
        Assert.All(result.Items, item => Assert.Equal("Smartphones", item.Category));
    }

    [Fact]
    public async Task Search_with_unknown_category_returns_200_with_empty_items_not_404()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/products?category=NoSuchCategory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task Search_result_items_include_specifications()
    {
        var client = fixture.Factory.CreateClient();

        // "Galaxy" alone now also matches "Galaxy Tab S9" (both seeded products), so disambiguate
        // with the full model name to keep asserting on exactly one result.
        var response = await client.GetAsync("/api/catalog/products?q=Galaxy%20S24");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        var galaxy = Assert.Single(result!.Items);
        Assert.Equal("Galaxy S24", galaxy.Name);
        Assert.Contains(galaxy.Specifications, s => s.Key == "camera_mp" && s.Value == "50");
    }

    [Fact]
    public async Task Search_query_combining_brand_and_model_still_matches_the_single_product()
    {
        var client = fixture.Factory.CreateClient();

        // The LLM often passes the user's own phrasing verbatim, which may combine brand and
        // model in ways a naive substring search never matches.
        var response = await client.GetAsync("/api/catalog/products?q=Samsung%20Galaxy%20S24");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        var galaxy = Assert.Single(result!.Items);
        Assert.Equal("Galaxy S24", galaxy.Name);
    }

    [Fact]
    public async Task Search_query_with_brand_and_model_concatenated_with_no_space_still_matches()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/products?q=GooglePixel%209");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        var pixel = Assert.Single(result!.Items);
        Assert.Equal("Pixel 9", pixel.Name);
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Contracts;
using TestSupport.SeedData;
using Xunit;

namespace ProductCatalog.Api.Tests;

public sealed class ParametricSearchContractTests(CatalogApiTestFixture fixture) : IClassFixture<CatalogApiTestFixture>
{
    [Fact]
    public async Task Gte_characteristic_filter_returns_only_products_meeting_the_threshold()
    {
        var client = fixture.Factory.CreateClient();

        var request = new ProductSearchRequest(
            Category: "Smartphones",
            Characteristics: [new CharacteristicFilter("camera_mp", CharacteristicFilterOperator.GreaterThanOrEqual, "50")]);

        var response = await client.PostAsJsonAsync("/api/catalog/products/search", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, item =>
            Assert.Contains(item.Specifications, s => s.Key == "camera_mp" && decimal.Parse(s.Value, CultureInfo.InvariantCulture) >= 50));
    }

    [Fact]
    public async Task Between_characteristic_filter_returns_only_products_within_range()
    {
        var client = fixture.Factory.CreateClient();

        var request = new ProductSearchRequest(
            Category: "Smartphones",
            Characteristics: [new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.Between, "3900", "4100")]);

        var response = await client.PostAsJsonAsync("/api/catalog/products/search", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        var galaxy = Assert.Single(result!.Items);
        Assert.Equal("Galaxy S24", galaxy.Name);
    }

    [Fact]
    public async Task Characteristic_filter_on_an_unknown_attribute_yields_zero_matches_not_an_error()
    {
        var client = fixture.Factory.CreateClient();

        var request = new ProductSearchRequest(
            Category: "Smartphones",
            Characteristics: [new CharacteristicFilter("waterproof_rating", CharacteristicFilterOperator.Equals, "IP68")]);

        var response = await client.PostAsJsonAsync("/api/catalog/products/search", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task Between_filter_missing_valueTo_returns_400()
    {
        var client = fixture.Factory.CreateClient();

        var request = new ProductSearchRequest(
            Category: "Smartphones",
            Characteristics: [new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.Between, "3900")]);

        var response = await client.PostAsJsonAsync("/api/catalog/products/search", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unrecognized_operator_returns_400()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/catalog/products/search", new
        {
            category = "Smartphones",
            characteristics = new[] { new { key = "camera_mp", @operator = "not_a_real_operator", value = "48" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CategoryId_filter_narrows_to_that_category_only()
    {
        var client = fixture.Factory.CreateClient();

        var request = new ProductSearchRequest(CategoryId: CatalogSeedData.LaptopsCategoryId);

        var response = await client.PostAsJsonAsync("/api/catalog/products/search", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, item => Assert.Equal("Laptops", item.Category));
    }
}

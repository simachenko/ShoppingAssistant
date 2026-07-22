using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Contracts;
using TestSupport.SeedData;
using Xunit;

namespace ProductCatalog.Api.Tests;

public sealed class ProductAndCategoryDetailContractTests(CatalogApiTestFixture fixture) : IClassFixture<CatalogApiTestFixture>
{
    [Fact]
    public async Task Known_product_id_returns_detail_including_description_and_isActive()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/catalog/products/{CatalogSeedData.GalaxyS24Id}");

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<ProductDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal("Galaxy S24", detail!.Name);
        Assert.Equal("Samsung", detail.Brand);
        Assert.True(detail.IsActive);
        Assert.NotEmpty(detail.Description);
        Assert.Contains(detail.Specifications, s => s.Key == "camera_mp" && s.Value == "50");
    }

    [Fact]
    public async Task Unknown_product_id_returns_404_not_a_fabricated_record()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/catalog/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Known_category_id_returns_its_comparable_attribute_keys()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/catalog/categories/{CatalogSeedData.SmartphonesCategoryId}");

        response.EnsureSuccessStatusCode();
        var category = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(category);
        Assert.Equal("Smartphones", category!.Name);
        Assert.Contains("camera_mp", category.ComparableAttributeKeys);
    }

    [Fact]
    public async Task Unknown_category_id_returns_404()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/catalog/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

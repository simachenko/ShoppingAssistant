using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Contracts;
using TestSupport.SeedData;
using Xunit;

namespace ProductCatalog.Api.Tests;

public sealed class CategoryByNameContractTests(CatalogApiTestFixture fixture) : IClassFixture<CatalogApiTestFixture>
{
    [Fact]
    public async Task Known_category_name_resolves_case_insensitively()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/categories?name=smartphones");

        response.EnsureSuccessStatusCode();
        var category = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(category);
        Assert.Equal(CatalogSeedData.SmartphonesCategoryId, category!.CategoryId);
        Assert.Equal("Smartphones", category.Name);
        Assert.Contains("camera_mp", category.ComparableAttributeKeys);
    }

    [Fact]
    public async Task Unknown_category_name_returns_404()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/categories?name=NoSuchCategory");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Missing_name_query_parameter_returns_400()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/categories");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

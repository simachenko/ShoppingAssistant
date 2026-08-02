using System.Net;
using Xunit;

namespace ProductCatalog.Api.Tests;

/// <summary>Proves FR-029: Catalog is never reachable without the internal service credential.</summary>
public sealed class InternalApiKeyAuthContractTests(CatalogApiTestFixture fixture) : IClassFixture<CatalogApiTestFixture>
{
    [Fact]
    public async Task Request_with_no_internal_api_key_header_is_rejected()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/categories?name=Smartphones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_with_the_wrong_internal_api_key_is_rejected()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            Microsoft.Extensions.Hosting.InternalApiKeyMiddleware.HeaderName, "not-the-real-key");

        var response = await client.GetAsync("/api/catalog/categories?name=Smartphones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_liveness_checks_never_require_the_internal_api_key()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/alive");

        response.EnsureSuccessStatusCode();
    }
}

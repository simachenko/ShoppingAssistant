using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ProductAdvisor.Domain;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>Proves FR-025/SC-015: <c>generate_checkout_link</c> resolves ids against Catalog and
/// builds a deterministic url encoding exactly the resolved products — never a guess.</summary>
public sealed class CheckoutLinkToolTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new(JsonSerializerDefaults.Web);

    private readonly AdvisorApiFactory _factory = new();

    private async Task<McpClient> CreateClientAsync()
    {
        var httpClient = _factory.CreateAuthenticatedClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient);
        return await McpClient.CreateAsync(transport);
    }

    private void SetUpOneKnownProduct(Guid productId)
    {
        _factory.CatalogResponder = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path == $"/api/catalog/products/{productId}"
                ? (HttpStatusCode.OK, new CatalogProductDto(
                    productId, "Galaxy S24", "Samsung", "Smartphones", Guid.NewGuid(), []))
                : (HttpStatusCode.NotFound, null);
        };
    }

    [Fact]
    public async Task Generate_checkout_link_encodes_exactly_the_resolved_product_ids()
    {
        var productId = Guid.NewGuid();
        SetUpOneKnownProduct(productId);

        var client = await CreateClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "generate_checkout_link");

        var result = await tool.CallAsync(new Dictionary<string, object?>
        {
            ["productIds"] = new[] { productId.ToString() },
        });

        Assert.NotEqual(true, result.IsError);
        var checkoutLink = JsonSerializer.Deserialize<CheckoutLink>(result.StructuredContent!.Value, DeserializeOptions);

        Assert.NotNull(checkoutLink);
        Assert.Equal([productId], checkoutLink!.ProductIds);
        Assert.Contains(productId.ToString(), checkoutLink.Url);
    }

    [Fact]
    public async Task Generate_checkout_link_omits_ids_that_dont_resolve_to_a_real_product()
    {
        var knownId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        SetUpOneKnownProduct(knownId);

        var client = await CreateClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "generate_checkout_link");

        var result = await tool.CallAsync(new Dictionary<string, object?>
        {
            ["productIds"] = new[] { knownId.ToString(), unknownId.ToString() },
        });

        Assert.NotEqual(true, result.IsError);
        var checkoutLink = JsonSerializer.Deserialize<CheckoutLink>(result.StructuredContent!.Value, DeserializeOptions);

        Assert.NotNull(checkoutLink);
        Assert.Equal([knownId], checkoutLink!.ProductIds);
        Assert.DoesNotContain(unknownId.ToString(), checkoutLink.Url);
    }

    [Fact]
    public async Task Generate_checkout_link_with_no_resolvable_ids_is_a_client_error()
    {
        _factory.CatalogResponder = _ => (HttpStatusCode.NotFound, null);

        var client = await CreateClientAsync();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "generate_checkout_link");

        var result = await tool.CallAsync(new Dictionary<string, object?>
        {
            ["productIds"] = new[] { Guid.NewGuid().ToString() },
        });

        Assert.True(result.IsError);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

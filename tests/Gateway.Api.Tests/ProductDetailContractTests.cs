using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Gateway.Api.Tests;

/// <summary>
/// Contract tests for the Gateway's plain product-detail composition endpoint (US3,
/// contracts/gateway-bff-api.md) — concurrent Catalog+Pricing fetch, outside the chat flow.
/// </summary>
public sealed class ProductDetailContractTests
{
    private static readonly Guid ProductId = Guid.Parse("00000000-0000-0000-0009-000000000005");

    [Fact]
    public async Task Product_detail_merges_catalog_and_pricing_into_one_response()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = request =>
            {
                Assert.Equal($"/api/catalog/products/{ProductId}", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.OK, $$"""
                    {
                      "productId": "{{ProductId}}", "name": "Galaxy S24", "brand": "Samsung",
                      "category": "Smartphones", "categoryId": "00000000-0000-0000-0002-000000000001",
                      "description": "Samsung's flagship smartphone.", "isActive": true,
                      "specifications": [ { "key": "camera_mp", "value": "50", "unit": "MP" } ]
                    }
                    """);
            },
            PricingResponder = request =>
            {
                Assert.Equal($"/api/pricing/offers/{ProductId}", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.OK, $$"""
                    {
                      "productId": "{{ProductId}}", "price": { "amount": 14500.00, "currency": "UAH" },
                      "discount": null, "availability": "InStock", "asOf": "2026-07-22T09:00:00Z", "source": "test"
                    }
                    """);
            },
        };

        var response = await factory.CreateAuthenticatedClient().GetAsync($"/api/products/{ProductId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Galaxy S24", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("priceVerified").GetBoolean());
        Assert.Equal(14500.00m, body.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.True(body.GetProperty("availabilityVerified").GetBoolean());
        Assert.Equal("InStock", body.GetProperty("availability").GetString());
    }

    [Fact]
    public async Task Product_detail_returns_404_when_catalog_has_no_such_product()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        var response = await factory.CreateAuthenticatedClient().GetAsync($"/api/products/{ProductId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Product_detail_degrades_to_unverified_price_when_pricing_is_unreachable()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "productId": "{{ProductId}}", "name": "Galaxy S24", "brand": "Samsung",
                  "category": "Smartphones", "categoryId": "00000000-0000-0000-0002-000000000001",
                  "description": "Samsung's flagship smartphone.", "isActive": true,
                  "specifications": []
                }
                """),
            PricingResponder = _ => throw new HttpRequestException("Pricing is unreachable."),
        };

        var response = await factory.CreateAuthenticatedClient().GetAsync($"/api/products/{ProductId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("priceVerified").GetBoolean());
        Assert.False(body.GetProperty("availabilityVerified").GetBoolean());
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}

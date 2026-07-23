using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Gateway.Api.Tests;

/// <summary>
/// Contract tests for the Gateway's explicit product-picker composition endpoints
/// (contracts/gateway-bff-api.md) — neither of these ever touches a conversation session or
/// the LLM.
/// </summary>
public sealed class ProductSearchAndCompareContractTests
{
    private static readonly Guid CheapProductId = Guid.Parse("00000000-0000-0000-0009-000000000001");
    private static readonly Guid ExpensiveProductId = Guid.Parse("00000000-0000-0000-0009-000000000002");
    private static readonly Guid UnpricedProductId = Guid.Parse("00000000-0000-0000-0009-000000000003");
    private static readonly Guid MidRangeProductId = Guid.Parse("00000000-0000-0000-0009-000000000004");

    [Fact]
    public async Task Search_returns_only_candidates_within_the_price_range_after_composing_pricing_offers()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = request =>
            {
                Assert.Equal("/api/catalog/products/search", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.OK, $$"""
                    {
                      "items": [
                        {{ProductJson(CheapProductId, "Budget Phone")}},
                        {{ProductJson(ExpensiveProductId, "Flagship Phone")}},
                        {{ProductJson(UnpricedProductId, "Unlisted Phone")}},
                        {{ProductJson(MidRangeProductId, "Mid-Range Phone")}}
                      ],
                      "page": 1, "pageSize": 20, "totalCount": 4
                    }
                    """);
            },
            PricingResponder = request =>
            {
                Assert.Equal("/api/pricing/offers", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.OK, $$"""
                    {
                      "offers": [
                        {{OfferJson(CheapProductId, 9000)}},
                        {{OfferJson(ExpensiveProductId, 90000)}},
                        {{OfferJson(MidRangeProductId, 25000)}}
                      ],
                      "notFound": ["{{UnpricedProductId}}"]
                    }
                    """);
            },
        };

        var response = await factory.CreateClient().GetAsync(
            "/api/products/search?category=Smartphones&priceMin=10000&priceMax=50000");
        response.EnsureSuccessStatusCode();

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var returnedIds = items.Select(i => i.GetProperty("productId").GetGuid()).ToList();

        Assert.Equal([MidRangeProductId], returnedIds);
    }

    [Fact]
    public async Task Search_candidate_within_range_is_returned_with_verified_price()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => JsonResponse(HttpStatusCode.OK, $$"""
                { "items": [ {{ProductJson(CheapProductId, "Budget Phone")}} ], "page": 1, "pageSize": 20, "totalCount": 1 }
                """),
            PricingResponder = _ => JsonResponse(HttpStatusCode.OK, $$"""
                { "offers": [ {{OfferJson(CheapProductId, 9000)}} ], "notFound": [] }
                """),
        };

        var response = await factory.CreateClient().GetAsync(
            "/api/products/search?category=Smartphones&priceMin=5000&priceMax=10000");
        response.EnsureSuccessStatusCode();

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();

        var item = Assert.Single(items);
        Assert.Equal(CheapProductId, item.GetProperty("productId").GetGuid());
        Assert.True(item.GetProperty("priceVerified").GetBoolean());
        Assert.Equal(9000, item.GetProperty("price").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Search_leaves_price_unverified_rather_than_failing_when_pricing_is_down()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => JsonResponse(HttpStatusCode.OK, $$"""
                { "items": [ {{ProductJson(CheapProductId, "Budget Phone")}} ], "page": 1, "pageSize": 20, "totalCount": 1 }
                """),
            PricingResponder = _ => throw new HttpRequestException("Pricing is unreachable."),
        };

        var response = await factory.CreateClient().GetAsync("/api/products/search?category=Smartphones");
        response.EnsureSuccessStatusCode();

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var item = Assert.Single(items);

        Assert.False(item.GetProperty("priceVerified").GetBoolean());
        Assert.False(item.GetProperty("availabilityVerified").GetBoolean());
    }

    [Fact]
    public async Task Search_relays_catalogs_400_for_an_unrecognized_characteristic_operator()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => JsonResponse(HttpStatusCode.BadRequest, "\"Unknown operator 'bogus'.\""),
        };

        var response = await factory.CreateClient().GetAsync(
            "/api/products/search?category=Smartphones&characteristics=camera_mp:bogus:50");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Compare_proxies_the_advisor_response_without_reshaping()
    {
        const string comparisonJson = """{"criteria":["price","camera_mp"],"rows":[{"productId":"00000000-0000-0000-0003-000000000001","name":"Galaxy S24","values":{"price":"14500.00 UAH","camera_mp":"50"},"rating":9.4,"deltasVsBest":{"price":"cheapest in set"}}],"explanation":null}""";

        var factory = new GatewayApiFactory
        {
            AdvisorResponder = request =>
            {
                Assert.Equal("/api/comparisons", request.RequestUri!.AbsolutePath);
                return JsonResponse(HttpStatusCode.OK, comparisonJson);
            },
        };

        string[] productIds = ["00000000-0000-0000-0003-000000000001", "00000000-0000-0000-0003-000000000006"];
        var response = await factory.CreateClient().PostAsJsonAsync("/api/products/compare", new
        {
            productIds,
            includeExplanation = false,
        });
        response.EnsureSuccessStatusCode();

        var actual = await response.Content.ReadFromJsonAsync<JsonElement>();
        var expected = JsonSerializer.Deserialize<JsonElement>(comparisonJson);

        Assert.Equal(expected.GetRawText(), actual.GetRawText());
    }

    [Fact]
    public async Task Compare_relays_the_advisors_400_for_too_few_product_ids()
    {
        var factory = new GatewayApiFactory
        {
            AdvisorResponder = _ => JsonResponse(HttpStatusCode.BadRequest, "\"At least 2 productIds are required.\""),
        };

        string[] productIds = ["00000000-0000-0000-0003-000000000001"];
        var response = await factory.CreateClient().PostAsJsonAsync("/api/products/compare", new { productIds });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string ProductJson(Guid productId, string name) => $$"""
        {
          "productId": "{{productId}}", "name": "{{name}}", "brand": "Acme", "category": "Smartphones",
          "categoryId": "00000000-0000-0000-0002-000000000001",
          "specifications": [ { "key": "camera_mp", "value": "50", "unit": "MP" } ]
        }
        """;

    private static string OfferJson(Guid productId, decimal amount) => $$"""
        {
          "productId": "{{productId}}", "price": { "amount": {{amount}}, "currency": "UAH" },
          "discount": null, "availability": "InStock", "asOf": "2026-07-22T09:00:00Z", "source": "test"
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}

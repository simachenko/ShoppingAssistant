using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Gateway.Api.Tests;

/// <summary>Proves FR-033/FR-034/FR-035/SC-020/SC-021: <c>GET /api/system-status</c> aggregates
/// each dependent service's own <c>/alive</c> check, is reachable without authentication, and
/// never itself fails even when a dependency is down.</summary>
public sealed class SystemStatusContractTests
{
    [Fact]
    public async Task Succeeds_with_no_authorization_header_at_all()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
            PricingResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
            AdvisorResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
        };

        var response = await factory.CreateClient().GetAsync("/api/system-status");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reports_ready_when_every_dependent_service_is_alive()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
            PricingResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
            AdvisorResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
        };

        var response = await factory.CreateClient().GetAsync("/api/system-status");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("ready", body.GetProperty("overall").GetString());
        var services = body.GetProperty("services").EnumerateArray().ToList();
        Assert.Equal(3, services.Count);
        Assert.All(services, s => Assert.True(s.GetProperty("reachable").GetBoolean()));
    }

    [Fact]
    public async Task Reports_degraded_with_200_not_5xx_when_one_dependent_service_is_down()
    {
        var factory = new GatewayApiFactory
        {
            CatalogResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
            PricingResponder = _ => throw new HttpRequestException("Pricing is unreachable."),
            AdvisorResponder = _ => new HttpResponseMessage(HttpStatusCode.OK),
        };

        var response = await factory.CreateClient().GetAsync("/api/system-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("degraded", body.GetProperty("overall").GetString());
        var services = body.GetProperty("services").EnumerateArray().ToList();
        var pricing = services.Single(s => s.GetProperty("name").GetString() == "pricing-api");
        Assert.False(pricing.GetProperty("reachable").GetBoolean());
        var others = services.Where(s => s.GetProperty("name").GetString() != "pricing-api");
        Assert.All(others, s => Assert.True(s.GetProperty("reachable").GetBoolean()));
    }
}

using Gateway.Api.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gateway.Api.Tests;

/// <summary>
/// Hosts the real Gateway with its <see cref="AdvisorApiClient"/> HttpClient swapped for a fake
/// handler — lets contract tests assert the Gateway's own composition/merge logic (correlation
/// ID propagation, session-creation-on-null, SSE proxying) without a live Advisor service.
/// </summary>
public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public Func<HttpRequestMessage, HttpResponseMessage> AdvisorResponder { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

    public Func<HttpRequestMessage, HttpResponseMessage> CatalogResponder { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

    public Func<HttpRequestMessage, HttpResponseMessage> PricingResponder { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AdvisorApiClient>();
            services.AddHttpClient<AdvisorApiClient>(client => client.BaseAddress = new Uri("http://advisor-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(req => AdvisorResponder(req)));

            services.RemoveAll<Gateway.Api.Clients.CatalogApiClient>();
            services.AddHttpClient<Gateway.Api.Clients.CatalogApiClient>(client => client.BaseAddress = new Uri("http://catalog-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(req => CatalogResponder(req)));

            services.RemoveAll<Gateway.Api.Clients.PricingApiClient>();
            services.AddHttpClient<Gateway.Api.Clients.PricingApiClient>(client => client.BaseAddress = new Uri("http://pricing-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(req => PricingResponder(req)));
        });
    }
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

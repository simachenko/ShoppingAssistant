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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AdvisorApiClient>();
            services.AddHttpClient<AdvisorApiClient>(client => client.BaseAddress = new Uri("http://advisor-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeAdvisorHttpMessageHandler(req => AdvisorResponder(req)));
        });
    }
}

internal sealed class FakeAdvisorHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

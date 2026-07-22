using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductAdvisor.Infrastructure.Clients;

namespace ProductAdvisor.Infrastructure;

public static class AdvisorHttpClientsExtensions
{
    public static IHostApplicationBuilder AddAdvisorHttpClients(this IHostApplicationBuilder builder)
    {
        // "http://catalog-api" / "http://pricing-api" resolve via Aspire service discovery
        // (AppHost resource names) — timeout/retry/circuit-breaker come from ServiceDefaults'
        // ConfigureHttpClientDefaults (constitution Principle V).
        builder.Services.AddHttpClient<CatalogClient>(client =>
            client.BaseAddress = new Uri("http://catalog-api"));

        builder.Services.AddHttpClient<PricingClient>(client =>
            client.BaseAddress = new Uri("http://pricing-api"));

        return builder;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Polly;
using ProductAdvisor.Infrastructure.Clients;

namespace ProductAdvisor.Infrastructure;

public static class AdvisorHttpClientsExtensions
{
    public static IHostApplicationBuilder AddAdvisorHttpClients(this IHostApplicationBuilder builder)
    {
        var catalogBaseAddress = GetServiceBaseAddress(builder.Configuration, "catalog-api");
        var pricingBaseAddress = GetServiceBaseAddress(builder.Configuration, "pricing-api");

        // Public HTTPS wakes Render free instances; outside Render, the logical HTTP names still
        // resolve through Aspire service discovery. Do not retry POST searches after a timeout.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental
        builder.Services.AddHttpClient<CatalogClient>(client =>
                client.BaseAddress = catalogBaseAddress)
            .RemoveAllResilienceHandlers()
            .AddResilienceHandler("catalog-cold-start", pipeline =>
                pipeline.AddTimeout(TimeSpan.FromMinutes(2)));

        builder.Services.AddHttpClient<PricingClient>(client =>
                client.BaseAddress = pricingBaseAddress)
            .RemoveAllResilienceHandlers()
            .AddResilienceHandler("pricing-cold-start", pipeline =>
                pipeline.AddTimeout(TimeSpan.FromMinutes(2)));
#pragma warning restore EXTEXP0001

        return builder;
    }

    private static Uri GetServiceBaseAddress(IConfiguration configuration, string serviceName)
    {
        var configuredHost = configuration[$"RenderExternalHosts:{serviceName}"];
        return string.IsNullOrWhiteSpace(configuredHost)
            ? new Uri($"http://{serviceName}")
            : new Uri(configuredHost.Contains("://", StringComparison.Ordinal)
                ? configuredHost
                : $"https://{configuredHost}");
    }
}

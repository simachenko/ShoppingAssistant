using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductAdvisor.Infrastructure;
using ProductAdvisor.Infrastructure.Clients;
using TestSupport;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Hosts the real <c>ProductAdvisor.Api</c> (MCP server + conversation API) with its
/// Catalog/Pricing HttpClients — and, for conversation-API-level tests, its IChatClient too —
/// swapped for fakes. No Docker/live LLM required for MCP tool contract tests; conversation API
/// tests that touch the DB pass a real Testcontainers connection string.
/// </summary>
public sealed class AdvisorApiFactory(string? connectionString = null) : WebApplicationFactory<Program>
{
    public Func<HttpRequestMessage, (HttpStatusCode, object?)> CatalogResponder { get; set; } = _ => (HttpStatusCode.NotFound, null);
    public Func<HttpRequestMessage, (HttpStatusCode, object?)> PricingResponder { get; set; } = _ => (HttpStatusCode.NotFound, null);
    public IChatClient? ChatClientOverride { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:advisordb"] = connectionString
                    ?? "Host=localhost;Database=advisordb_test;Username=test;Password=test",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CatalogClient>();
            services.RemoveAll<PricingClient>();

            services.AddHttpClient<CatalogClient>(client => client.BaseAddress = new Uri("http://catalog-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeJsonHttpMessageHandler(req => CatalogResponder(req)));

            services.AddHttpClient<PricingClient>(client => client.BaseAddress = new Uri("http://pricing-api"))
                .ConfigurePrimaryHttpMessageHandler(() => new FakeJsonHttpMessageHandler(req => PricingResponder(req)));

            if (ChatClientOverride is not null)
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton(ChatClientOverride);
            }
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
        await db.Database.MigrateAsync();
    }
}

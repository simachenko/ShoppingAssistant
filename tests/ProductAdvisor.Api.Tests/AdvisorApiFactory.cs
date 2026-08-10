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

    /// <summary>Overrides the hosted app's <c>IHostEnvironment.EnvironmentName</c> — defaults to
    /// <c>WebApplicationFactory</c>'s own "Development", overridden only by tests proving
    /// Production-specific behavior (e.g. <c>InternalApiKeyMiddleware</c>'s rejection of the
    /// local-development placeholder value, FR-127).</summary>
    public string? EnvironmentName { get; set; }

    /// <summary>Overrides the <c>InternalApiKey</c> configuration value the hosted app expects —
    /// defaults to <see cref="InternalApiKeyTestDefaults.Key"/>; a credential-hardening test sets
    /// this directly to exercise a specific value (e.g. the local-development placeholder, or
    /// null to prove the unset case) rather than only ever varying what the *client* sends.</summary>
    public string? InternalApiKeyOverride { get; set; } = InternalApiKeyTestDefaults.Key;

    /// <summary>Sets <c>InternalApiKeyPrevious</c> — a rotation test's "still-valid old value"
    /// (FR-128). Unset (null) by default, matching production's default no-rotation-in-progress state.</summary>
    public string? PreviousInternalApiKeyOverride { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (EnvironmentName is not null)
        {
            builder.UseEnvironment(EnvironmentName);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:advisordb"] = connectionString
                    ?? "Host=localhost;Database=advisordb_test;Username=test;Password=test",
                ["InternalApiKey"] = InternalApiKeyOverride,
                ["InternalApiKeyPrevious"] = PreviousInternalApiKeyOverride,
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

    /// <summary>Default caller identity for <see cref="CreateAuthenticatedClient"/> — most tests
    /// don't care which user they are, only that they're a valid, consistent one. A dedicated
    /// session-ownership test uses two distinct ids instead of this shared one.</summary>
    public const string DefaultTestUserId = "test-user";

    /// <summary>
    /// Pre-authenticated with the internal API key the hosted app expects (see
    /// <see cref="ConfigureWebHost"/>) and a default <c>X-User-Id</c> (<see cref="DefaultTestUserId"/>)
    /// — existing contract tests use this instead of the inherited <c>CreateClient()</c> now that
    /// session-scoped endpoints require both. A dedicated rejection/ownership test builds its own
    /// <see cref="HttpClient"/> with different headers instead.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            Microsoft.Extensions.Hosting.InternalApiKeyMiddleware.HeaderName, InternalApiKeyTestDefaults.Key);
        client.DefaultRequestHeaders.Add("X-User-Id", DefaultTestUserId);
        return client;
    }
}

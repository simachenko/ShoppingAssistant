using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PricingAvailability.Infrastructure;
using TestSupport.SeedData;

namespace PricingAvailability.Api.Tests;

/// <summary>Points the real <c>PricingAvailability.Api</c> host at a Testcontainers Postgres instance.</summary>
public sealed class PricingApiFactory : WebApplicationFactory<Program>
{
    public PricingApiFactory(string connectionString)
    {
        // Program.cs calls builder.AddNpgsqlDbContext<PricingDbContext>("pricingdb") as a
        // top-level statement, which reads ConnectionStrings:pricingdb synchronously at
        // registration time. WebApplicationFactory's ConfigureWebHost/ConfigureAppConfiguration
        // hooks only apply once WebApplicationBuilder.Build() runs, which is too late for that
        // eager read — an in-memory config override there is silently ignored, and the host ends
        // up on whatever appsettings.Development.json's pricingdb connection string points at
        // (the real, possibly already-seeded, docker-compose Postgres on localhost:5432) instead
        // of this test's isolated Testcontainers instance. Environment variables are loaded
        // synchronously inside WebApplication.CreateBuilder itself, so setting one here — before
        // Services is ever touched and Program.cs's top-level code actually runs — is visible in
        // time for AddNpgsqlDbContext's eager read.
        Environment.SetEnvironmentVariable("ConnectionStrings__pricingdb", connectionString);
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
        await db.Database.MigrateAsync();

        db.Offers.AddRange(PricingSeedData.Offers);
        await db.SaveChangesAsync();
    }
}

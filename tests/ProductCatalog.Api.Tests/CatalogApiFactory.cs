using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Infrastructure;
using TestSupport.SeedData;

namespace ProductCatalog.Api.Tests;

/// <summary>Points the real <c>ProductCatalog.Api</c> host at a Testcontainers Postgres instance.</summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    public CatalogApiFactory(string connectionString)
    {
        // Program.cs calls builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb") as a
        // top-level statement, which reads ConnectionStrings:catalogdb synchronously at
        // registration time. WebApplicationFactory's ConfigureWebHost/ConfigureAppConfiguration
        // hooks only apply once WebApplicationBuilder.Build() runs, which is too late for that
        // eager read — an in-memory config override there is silently ignored, and the host ends
        // up on whatever appsettings.Development.json's catalogdb connection string points at
        // (the real, possibly already-seeded, docker-compose Postgres on localhost:5432) instead
        // of this test's isolated Testcontainers instance. Environment variables are loaded
        // synchronously inside WebApplication.CreateBuilder itself, so setting one here — before
        // Services is ever touched and Program.cs's top-level code actually runs — is visible in
        // time for AddNpgsqlDbContext's eager read.
        Environment.SetEnvironmentVariable("ConnectionStrings__catalogdb", connectionString);
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.MigrateAsync();

        db.Brands.AddRange(CatalogSeedData.Brands);
        db.Categories.AddRange(CatalogSeedData.Categories);
        db.Products.AddRange(CatalogSeedData.Products);
        await db.SaveChangesAsync();
    }
}

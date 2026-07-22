using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Infrastructure;
using TestSupport.SeedData;

namespace ProductCatalog.Api.Tests;

/// <summary>Points the real <c>ProductCatalog.Api</c> host at a Testcontainers Postgres instance.</summary>
public sealed class CatalogApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:catalogdb"] = connectionString,
            });
        });
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

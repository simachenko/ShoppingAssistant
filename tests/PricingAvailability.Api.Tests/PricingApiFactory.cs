using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PricingAvailability.Infrastructure;
using TestSupport.SeedData;

namespace PricingAvailability.Api.Tests;

/// <summary>Points the real <c>PricingAvailability.Api</c> host at a Testcontainers Postgres instance.</summary>
public sealed class PricingApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:pricingdb"] = connectionString,
            });
        });
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

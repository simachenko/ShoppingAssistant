using Microsoft.EntityFrameworkCore;
using Npgsql;
using PricingAvailability.Infrastructure;
using ProductCatalog.Infrastructure;
using TestSupport.SeedData;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Connects to the docker-compose stack started via `docker compose up --build` (see
/// quickstart.md) and seeds it with <see cref="CatalogSeedData"/>/<see cref="PricingSeedData"/>
/// if not already present, so every scenario test can rely on the same fixed products.
/// Requires the stack to already be running — this fixture does not start it.
///
/// The catalog/pricing containers seed themselves on startup with the same fixed GUIDs
/// (SeedDemoData=true in docker-compose.yml), so this fixture's own AnyAsync-then-insert can
/// race a concurrently-starting container's identical check-then-insert. The AnyAsync guard
/// narrows the window but can't close it, so a losing insert here is treated as evidence the
/// other writer already seeded the data, not a real failure.
/// </summary>
public sealed class DockerComposeStackFixture : IAsyncLifetime
{
    public const string GatewayBaseUrl = "http://localhost:5100";

    private const string CatalogConnectionString =
        "Host=localhost;Port=5432;Database=catalogdb;Username=catalog_role;Password=catalog_dev_password";
    private const string PricingConnectionString =
        "Host=localhost;Port=5432;Database=pricingdb;Username=pricing_role;Password=pricing_dev_password";

    public async Task InitializeAsync()
    {
        await using var catalogDb = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(CatalogConnectionString).Options);
        await catalogDb.Database.MigrateAsync();

        if (!await catalogDb.Products.AnyAsync())
        {
            catalogDb.Brands.AddRange(CatalogSeedData.Brands);
            catalogDb.Categories.AddRange(CatalogSeedData.Categories);
            catalogDb.Products.AddRange(CatalogSeedData.Products);
            await TrySaveChangesAsync(catalogDb);
        }

        await using var pricingDb = new PricingDbContext(
            new DbContextOptionsBuilder<PricingDbContext>().UseNpgsql(PricingConnectionString).Options);
        await pricingDb.Database.MigrateAsync();

        if (!await pricingDb.Offers.AnyAsync())
        {
            pricingDb.Offers.AddRange(PricingSeedData.Offers);
            await TrySaveChangesAsync(pricingDb);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task TrySaveChangesAsync(DbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another writer (the catalog/pricing container's own startup seed, or a concurrent
            // test run) won the race and inserted this fixed-GUID data first — the desired end
            // state (seeded) is already achieved, so this isn't a real failure.
        }
    }
}

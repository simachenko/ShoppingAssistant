using TestSupport;
using Xunit;

namespace ProductCatalog.Api.Tests;

public sealed class CatalogApiTestFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public CatalogApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new CatalogApiFactory(_postgres.ConnectionString);
        await Factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

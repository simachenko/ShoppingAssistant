using TestSupport;
using Xunit;

namespace PricingAvailability.Api.Tests;

public sealed class PricingApiTestFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public PricingApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new PricingApiFactory(_postgres.ConnectionString);
        await Factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

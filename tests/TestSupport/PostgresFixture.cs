using Testcontainers.PostgreSql;
using Xunit;

namespace TestSupport;

/// <summary>
/// A throwaway, real Postgres instance for infrastructure/contract tests — reused by
/// ProductCatalog.Api.Tests, PricingAvailability.Api.Tests, and ProductAdvisor.Api.Tests so
/// each service's EF Core mapping is verified against the real engine, not an in-memory fake.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("testdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

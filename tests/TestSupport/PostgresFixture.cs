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
    // pgvector's image rather than stock postgres:16-alpine: ProductAdvisor's migrations declare a
    // `vector` column type, so its CREATE EXTENSION vector fails against an image where the
    // extension binary is not installed at all. A strict superset of Postgres 16 — Catalog and
    // Pricing, which use no extension, behave identically on it.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("testdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

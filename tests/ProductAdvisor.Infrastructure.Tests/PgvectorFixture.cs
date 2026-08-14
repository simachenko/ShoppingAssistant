using Testcontainers.PostgreSql;
using Xunit;

namespace ProductAdvisor.Infrastructure.Tests;

/// <summary>
/// A throwaway Postgres with the pgvector extension available.
/// </summary>
/// <remarks>
/// Deliberately not <c>TestSupport.PostgresFixture</c>: that one runs <c>postgres:16-alpine</c>,
/// which has no pgvector, so the store-info migration's <c>CREATE EXTENSION vector</c> would fail
/// against it. The store-info schema is the first thing in this solution that needs an extension
/// beyond stock Postgres, so it gets its own image rather than changing the image every other
/// service's tests already depend on.
/// </remarks>
public sealed class PgvectorFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("advisordb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PgvectorCollection : ICollectionFixture<PgvectorFixture>
{
    public const string Name = "pgvector";
}

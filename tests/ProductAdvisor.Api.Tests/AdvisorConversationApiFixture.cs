using TestSupport;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class AdvisorConversationApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public string ConnectionString => _postgres.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();

        await using var factory = new AdvisorApiFactory(ConnectionString);
        await factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

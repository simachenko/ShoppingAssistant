using System.Net;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>Proves FR-029: Advisor is never reachable without the internal service credential.</summary>
public sealed class InternalApiKeyAuthContractTests : IAsyncDisposable
{
    private readonly AdvisorApiFactory _factory = new();

    [Fact]
    public async Task Request_with_no_internal_api_key_header_is_rejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/conversations", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_with_the_wrong_internal_api_key_is_rejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            Microsoft.Extensions.Hosting.InternalApiKeyMiddleware.HeaderName, "not-the-real-key");

        var response = await client.PostAsync("/api/conversations", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_liveness_checks_never_require_the_internal_api_key()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/alive");

        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

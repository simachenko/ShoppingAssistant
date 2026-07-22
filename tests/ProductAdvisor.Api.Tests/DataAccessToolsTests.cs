using System.Net;
using ModelContextProtocol.Client;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Exercises search_products / get_product_details / check_price_and_availability through a
/// real in-process MCP client over the hosted /mcp endpoint — proving the transport/schema
/// layer works, not just the underlying C# methods (contracts/advisor-mcp-tools.md).
/// </summary>
public sealed class DataAccessToolsTests : IAsyncDisposable
{
    private readonly AdvisorApiFactory _factory = new();
    private McpClient? _client;

    private async Task<McpClient> GetClientAsync()
    {
        if (_client is not null)
        {
            return _client;
        }

        var httpClient = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient);

        _client = await McpClient.CreateAsync(transport);
        return _client;
    }

    [Fact]
    public async Task Tool_list_advertises_all_data_access_and_compute_tools()
    {
        var client = await GetClientAsync();

        var tools = await client.ListToolsAsync();

        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("search_products", names);
        Assert.Contains("get_product_details", names);
        Assert.Contains("check_price_and_availability", names);
        Assert.Contains("get_recommendations", names);
        Assert.Contains("compare_products", names);
    }

    [Fact]
    public async Task Get_product_details_for_unknown_id_returns_found_false_not_a_transport_error()
    {
        _factory.CatalogResponder = _ => (HttpStatusCode.NotFound, null);
        var client = await GetClientAsync();
        var tools = await client.ListToolsAsync();
        var tool = tools.Single(t => t.Name == "get_product_details");

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["productId"] = Guid.NewGuid().ToString() });

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.False(result.StructuredContent!.Value.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task Check_price_and_availability_with_empty_ids_is_a_client_error()
    {
        var client = await GetClientAsync();
        var tools = await client.ListToolsAsync();
        var tool = tools.Single(t => t.Name == "check_price_and_availability");

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["productIds"] = Array.Empty<string>() });

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Check_price_and_availability_over_limit_is_a_client_error()
    {
        var client = await GetClientAsync();
        var tools = await client.ListToolsAsync();
        var tool = tools.Single(t => t.Name == "check_price_and_availability");
        var tooMany = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid().ToString()).ToArray();

        var result = await tool.CallAsync(new Dictionary<string, object?> { ["productIds"] = tooMany });

        Assert.True(result.IsError);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }
}

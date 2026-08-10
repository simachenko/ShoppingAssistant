using System.Net;
using System.Reflection;
using ModelContextProtocol.Server;
using ProductAdvisor.Infrastructure.Tools;
using TestSupport;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 13 (spec.md FR-131): a valid internal credential never grants an MCP caller conversation
/// ownership on its own — the FR-031 ownership check is never bypassable from the MCP transport.
/// NOT run in this sandbox — like the rest of this test project, it requires a Testcontainers
/// Postgres instance (Docker), unavailable here.
/// </summary>
public sealed class McpOwnershipIndependenceTests : IAsyncDisposable
{
    private readonly AdvisorApiFactory _factory = new();

    [Fact]
    public async Task The_mcp_endpoint_still_requires_the_internal_api_key_regardless_of_X_User_Id()
    {
        // /mcp sits behind the same InternalApiKeyMiddleware as every other endpoint (Program.cs
        // registers app.UseInternalApiKeyAuth() once, ahead of app.MapMcp("/mcp")) — an arbitrary
        // X-User-Id alone, without the internal key, is rejected the same as anywhere else.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "someone-elses-user-id");

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void No_registered_MCP_tool_accepts_a_sessionId_that_a_valid_internal_key_could_use_to_bypass_FR_031()
    {
        // FR-131's guarantee is preserved structurally today, not by a runtime check with
        // something to intercept: none of the seven MCP tools registered on DataAccessTools/
        // ComputeTools takes a sessionId (or any conversation-scoped) parameter at all — every
        // one operates on catalog/pricing data or an already-resolved product-id set, never on
        // ConversationSession. This test fails the moment that stops being true, forcing whoever
        // adds a session-scoped MCP tool to also add the ownership check FR-031 requires, rather
        // than silently losing this guarantee.
        var toolMethods = new[] { typeof(DataAccessTools), typeof(ComputeTools) }
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var method in toolMethods)
        {
            var parameterNames = method.GetParameters().Select(p => p.Name?.ToLowerInvariant());
            Assert.DoesNotContain("sessionid", parameterNames);
        }
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 12 (spec.md FR-104/FR-107/FR-113): oversized or control-character-bearing input never
/// reaches an LLM/tool call. NOT run in this sandbox — like the rest of this test project, it
/// requires a Testcontainers Postgres instance (Docker), unavailable here; verified by
/// inspection and by the runnable unit-level coverage in
/// <c>ProductAdvisor.Application.Tests.InputValidationStageTests</c>/
/// <c>ConversationOrchestratorGuardrailTests</c> instead.
/// </summary>
public sealed class InputGuardrailTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    [Fact]
    public async Task An_oversized_message_returns_400_with_zero_llm_calls()
    {
        var chatClient = new ExtractionAwareScriptedChatClient("unused", "unused");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var oversized = new string('a', 10_000);
        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest(oversized));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(chatClient.OfferedToolNamesPerCall);
    }

    [Fact]
    public async Task A_message_with_rejected_control_characters_returns_400_with_zero_llm_calls()
    {
        var chatClient = new ExtractionAwareScriptedChatClient("unused", "unused");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("HelloWorld"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(chatClient.OfferedToolNamesPerCall);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 12 (spec.md FR-116): a PII fixture never reaches the extraction call verbatim —
/// `Blocked` returns `400` with zero LLM calls; `Redacted` proceeds, but only the redacted text
/// is ever offered to the extraction call. NOT run in this sandbox — like the rest of this test
/// project, it requires a Testcontainers Postgres instance (Docker), unavailable here; verified
/// by inspection and by the runnable unit-level coverage in
/// <c>ProductAdvisor.Application.Tests.PiiScreeningStageTests</c>/
/// <c>ConversationOrchestratorGuardrailTests</c> instead.
/// </summary>
public sealed class PiiScreeningTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    [Fact]
    public async Task A_credit_card_shaped_message_is_blocked_with_zero_llm_calls()
    {
        var chatClient = new ExtractionAwareScriptedChatClient("unused", "unused");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages",
            new SendMessageRequest("Please charge my card 4111 1111 1111 1111 for this order"));

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

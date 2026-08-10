using System.Net.Http.Json;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 12 (spec.md FR-108): an invalid currency or a negative budget in extraction output
/// routes to `clarification`, never a tool call — <c>Money.TryCreate</c> rejects the value inside
/// <c>ExtractionStage.ToDomain</c> instead of throwing and crashing the turn. NOT run in this
/// sandbox — like the rest of this test project, it requires a Testcontainers Postgres instance
/// (Docker), unavailable here; verified by inspection and by the runnable unit-level coverage in
/// <c>ProductAdvisor.Domain.Tests.MoneyTests</c> instead.
/// </summary>
public sealed class StrictValueValidationTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    [Fact]
    public async Task An_unrecognized_currency_in_extraction_output_routes_to_clarification_never_a_tool_call()
    {
        var chatClient = new ExtractionAwareScriptedChatClient(
            """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":15000,"budgetCurrency":"XYZ"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""",
            "unused");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("I need a smartphone for 15000 XYZ"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.Equal("clarification", body!.Type);
        Assert.Single(chatClient.OfferedToolNamesPerCall); // extraction only — no tool call reached.
    }

    [Fact]
    public async Task A_negative_budget_in_extraction_output_routes_to_clarification_never_a_tool_call()
    {
        var chatClient = new ExtractionAwareScriptedChatClient(
            """{"intent":"recommend","requirementPatch":{"category":"Smartphones","budgetAmount":-500,"budgetCurrency":"UAH"},"productReferences":[],"missingFields":[],"confidence":0.9,"language":"en"}""",
            "unused");
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString) { ChatClientOverride = chatClient };
        var client = factory.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("I need a smartphone for -500 UAH"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConversationTurnResponse>();
        Assert.Equal("clarification", body!.Type);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }
}

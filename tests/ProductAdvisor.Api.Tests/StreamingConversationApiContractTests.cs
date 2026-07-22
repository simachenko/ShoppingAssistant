using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class StreamingConversationApiContractTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    private static readonly string[] RequiredCameraFeature = ["camera_mp"];

    // The SSE `result` event is hand-serialized with Web (camelCase) options to match what
    // Results.Ok(...) produces for the non-streaming endpoint — read it back the same way.
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Concatenated_token_deltas_equal_the_final_results_message()
    {
        var (client, sessionId, _) = await StartRecommendationSessionAsync(throwMidStream: false);

        var (tokens, result) = await ReadStreamAsync(client, sessionId,
            "I need a smartphone with a good camera and a budget of up to 15000 UAH");

        Assert.NotNull(result);
        Assert.Equal("recommendation", result!.Type);
        Assert.Equal(result.Message, string.Concat(tokens));
        Assert.NotEmpty(tokens);
    }

    [Fact]
    public async Task Streamed_result_structured_fields_are_byte_identical_to_the_non_streaming_endpoint()
    {
        var productId = Guid.NewGuid();
        var narration = "Here's a smartphone within your budget with a great camera.";

        Func<AdvisorApiFactory> makeFactory = () => new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse(
                [new CatalogProductDto(productId, "Galaxy S24", "Samsung", "Smartphones",
                    [new CatalogSpecificationDto("camera_mp", "50", "MP")])], 1, 50, 1)),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [new PricingOfferDto(productId, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed")], [])),
            ChatClientOverride = new ScriptedChatClient(
                "get_recommendations",
                new Dictionary<string, object?>
                {
                    ["category"] = "Smartphones",
                    ["budgetAmount"] = 15000m,
                    ["budgetCurrency"] = "UAH",
                    ["requiredFeatures"] = RequiredCameraFeature,
                },
                narration),
        };

        const string userMessage = "I need a smartphone with a good camera and a budget of up to 15000 UAH";

        await using var nonStreamingFactory = makeFactory();
        var nonStreamingClient = nonStreamingFactory.CreateClient();
        var nonStreamingSessionId = await CreateSessionAsync(nonStreamingClient);
        var nonStreamingResponse = await nonStreamingClient.PostAsJsonAsync(
            $"/api/conversations/{nonStreamingSessionId}/messages", new SendMessageRequest(userMessage));
        nonStreamingResponse.EnsureSuccessStatusCode();
        var nonStreamingBody = await nonStreamingResponse.Content.ReadFromJsonAsync<ConversationTurnResponse>();

        await using var streamingFactory = makeFactory();
        var streamingClient = streamingFactory.CreateClient();
        var streamingSessionId = await CreateSessionAsync(streamingClient);
        var (_, streamedResult) = await ReadStreamAsync(streamingClient, streamingSessionId, userMessage);

        Assert.NotNull(nonStreamingBody);
        Assert.NotNull(streamedResult);

        // Record equality doesn't do a deep compare of the List<T> members (MatchedRequirements/
        // TradeOffs use reference equality), so compare the same way the contract phrases the
        // guarantee: serialized JSON of the structured fields is byte-identical either way.
        Assert.Equal(JsonSerializer.Serialize(nonStreamingBody!.Items), JsonSerializer.Serialize(streamedResult!.Items));
        Assert.Equal(nonStreamingBody.UnmetConstraintExplanation, streamedResult.UnmetConstraintExplanation);
    }

    [Fact]
    public async Task A_stream_interrupted_before_its_result_event_is_detectable_as_incomplete()
    {
        var (client, sessionId, _) = await StartRecommendationSessionAsync(throwMidStream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{sessionId}/messages/stream")
        {
            Content = JsonContent.Create(new SendMessageRequest(
                "I need a smartphone with a good camera and a budget of up to 15000 UAH")),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var sawResult = false;
        await using var stream = await response.Content.ReadAsStreamAsync();

        // The simulated provider failure aborts the connection before a `result` event is ever
        // written — a well-behaved client must observe either an error while reading the stream,
        // or the stream ending with no `result` event seen, never a false "it completed normally".
        try
        {
            await foreach (var item in SseParser.Create(stream).EnumerateAsync())
            {
                if (item.EventType == "result")
                {
                    sawResult = true;
                }
            }
        }
        catch (IOException)
        {
            // Expected: the connection was cut mid-response.
        }
        catch (HttpRequestException)
        {
            // Expected: the connection was cut mid-response.
        }

        Assert.False(sawResult);
    }

    private async Task<(HttpClient Client, Guid SessionId, AdvisorApiFactory Factory)> StartRecommendationSessionAsync(bool throwMidStream)
    {
        var productId = Guid.NewGuid();
        var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = _ => (HttpStatusCode.OK, new CatalogSearchResponse(
                [new CatalogProductDto(productId, "Galaxy S24", "Samsung", "Smartphones",
                    [new CatalogSpecificationDto("camera_mp", "50", "MP")])], 1, 50, 1)),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [new PricingOfferDto(productId, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed")], [])),
            ChatClientOverride = new ScriptedChatClient(
                "get_recommendations",
                new Dictionary<string, object?>
                {
                    ["category"] = "Smartphones",
                    ["budgetAmount"] = 15000m,
                    ["budgetCurrency"] = "UAH",
                    ["requiredFeatures"] = RequiredCameraFeature,
                },
                "Here's a smartphone within your budget with a great camera.",
                throwMidStream),
        };
        var client = factory.CreateClient();
        var sessionId = await CreateSessionAsync(client);
        return (client, sessionId, factory);
    }

    private static async Task<(List<string> Tokens, ConversationTurnResponse? Result)> ReadStreamAsync(
        HttpClient client, Guid sessionId, string text)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{sessionId}/messages/stream")
        {
            Content = JsonContent.Create(new SendMessageRequest(text)),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var tokens = new List<string>();
        ConversationTurnResponse? result = null;
        await using var stream = await response.Content.ReadAsStreamAsync();

        await foreach (var item in SseParser.Create(stream).EnumerateAsync())
        {
            if (item.EventType == "token")
            {
                using var doc = JsonDocument.Parse(item.Data);
                tokens.Add(doc.RootElement.GetProperty("delta").GetString() ?? "");
            }
            else if (item.EventType == "result")
            {
                result = JsonSerializer.Deserialize<ConversationTurnResponse>(item.Data, ResultJsonOptions);
            }
        }

        return (tokens, result);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/conversations", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetGuid();
    }
}

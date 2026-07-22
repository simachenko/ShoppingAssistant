using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Gateway.Api.Tests;

public sealed class StreamingChatContractTests
{
    [Fact]
    public async Task Resolved_sessionId_is_merged_into_the_result_event()
    {
        var (client, sessionCreationCalls) = SetUpStreamingAdvisor(out var advisorSessionId);

        var (tokens, result) = await StreamChatAsync(client, sessionId: null, "I need a good laptop");

        Assert.Equal("Hi there", string.Concat(tokens));
        Assert.NotNull(result);
        Assert.Equal(advisorSessionId, result!.Value.GetProperty("sessionId").GetGuid());
        Assert.Equal("What's your budget?", result.Value.GetProperty("question").GetString());
        Assert.Equal(1, sessionCreationCalls());
    }

    [Fact]
    public async Task A_null_sessionId_creates_exactly_one_new_session_on_the_streaming_path()
    {
        var (client, sessionCreationCalls) = SetUpStreamingAdvisor(out _);

        await StreamChatAsync(client, sessionId: null, "I need a good laptop");

        Assert.Equal(1, sessionCreationCalls());
    }

    private static (HttpClient Client, Func<int> SessionCreationCalls) SetUpStreamingAdvisor(out Guid advisorSessionId)
    {
        var sessionId = Guid.NewGuid();
        advisorSessionId = sessionId;
        var calls = 0;

        var factory = new GatewayApiFactory
        {
            AdvisorResponder = request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/conversations")
                {
                    calls++;
                    return JsonResponse(HttpStatusCode.Created, $$"""{"sessionId":"{{sessionId}}"}""");
                }

                if (request.RequestUri!.AbsolutePath == $"/api/conversations/{sessionId}/messages/stream")
                {
                    return SseResponse(
                        "event: token\ndata: {\"delta\":\"Hi \"}\n\n" +
                        "event: token\ndata: {\"delta\":\"there\"}\n\n" +
                        "event: result\ndata: {\"type\":\"clarification\",\"question\":\"What's your budget?\"}\n\n");
                }

                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}.");
            },
        };

        return (factory.CreateClient(), () => calls);
    }

    private static async Task<(List<string> Tokens, JsonElement? Result)> StreamChatAsync(HttpClient client, Guid? sessionId, string text)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/messages/stream")
        {
            Content = JsonContent.Create(new { sessionId, text }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var tokens = new List<string>();
        JsonElement? result = null;
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
                result = JsonSerializer.Deserialize<JsonElement>(item.Data);
            }
        }

        return (tokens, result);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage SseResponse(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return response;
    }
}

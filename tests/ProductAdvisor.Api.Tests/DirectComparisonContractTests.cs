using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ProductAdvisor.Application.Contracts;
using ProductAdvisor.Infrastructure.Clients;
using Xunit;

namespace ProductAdvisor.Api.Tests;

public sealed class DirectComparisonContractTests(AdvisorConversationApiFixture fixture) : IClassFixture<AdvisorConversationApiFixture>
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Direct_endpoint_and_conversational_path_return_byte_identical_criteria_and_rows()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        Func<AdvisorApiFactory> makeFactory = () => new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == $"/api/catalog/products/{productA}")
                {
                    return (HttpStatusCode.OK, new CatalogProductDto(productA, "Galaxy S24", "Samsung", "Smartphones",
                        categoryId, [new CatalogSpecificationDto("camera_mp", "50", "MP")]));
                }

                if (path == $"/api/catalog/products/{productB}")
                {
                    return (HttpStatusCode.OK, new CatalogProductDto(productB, "Pixel 9", "Google", "Smartphones",
                        categoryId, [new CatalogSpecificationDto("camera_mp", "48", "MP")]));
                }

                if (path == $"/api/catalog/categories/{categoryId}")
                {
                    return (HttpStatusCode.OK, new CatalogCategoryDto(categoryId, "Smartphones", ["camera_mp"]));
                }

                return (HttpStatusCode.NotFound, null);
            },
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [
                    new PricingOfferDto(productA, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                    new PricingOfferDto(productB, new PricingMoneyDto(13500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                ], [])),
        };

        // Direct path — no chat client involved at all.
        await using var directFactory = makeFactory();
        directFactory.ChatClientOverride = new SpyChatClient(callsAreErrors: true);
        var directClient = directFactory.CreateClient();
        var directResponse = await directClient.PostAsJsonAsync("/api/comparisons", new DirectComparisonRequest(
            [productA.ToString(), productB.ToString()], IncludeExplanation: false));
        directResponse.EnsureSuccessStatusCode();
        var directBody = await directResponse.Content.ReadFromJsonAsync<DirectComparisonResponse>();

        // Conversational path — a scripted chat client calls compare_products with the same ids.
        await using var conversationalFactory = makeFactory();
        conversationalFactory.ChatClientOverride = new ScriptedChatClient(
            "compare_products",
            new Dictionary<string, object?> { ["productIds"] = new[] { productA.ToString(), productB.ToString() } },
            "Here's how they compare.");
        var conversationalClient = conversationalFactory.CreateClient();
        var sessionResponse = await conversationalClient.PostAsync("/api/conversations", content: null);
        var sessionId = (await sessionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetGuid();
        var conversationalResponse = await conversationalClient.PostAsJsonAsync(
            $"/api/conversations/{sessionId}/messages", new SendMessageRequest("Compare these two"));
        conversationalResponse.EnsureSuccessStatusCode();
        var conversationalBody = await conversationalResponse.Content.ReadFromJsonAsync<ConversationTurnResponse>();

        Assert.NotNull(directBody);
        Assert.NotNull(conversationalBody);
        Assert.Equal("comparison", conversationalBody!.Type);
        Assert.Equal(directBody!.Criteria, conversationalBody.Criteria);
        Assert.Equal(JsonSerializer.Serialize(directBody.Rows), JsonSerializer.Serialize(conversationalBody.Rows));
    }

    [Fact]
    public async Task IncludeExplanation_false_makes_zero_chat_client_calls()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var spy = new SpyChatClient(callsAreErrors: true);

        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = request => RespondForTwoProducts(request, productA, productB, categoryId),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [
                    new PricingOfferDto(productA, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                    new PricingOfferDto(productB, new PricingMoneyDto(13500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                ], [])),
            ChatClientOverride = spy,
        };
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/comparisons", new DirectComparisonRequest(
            [productA.ToString(), productB.ToString()], IncludeExplanation: false));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DirectComparisonResponse>();
        Assert.NotNull(body);
        Assert.Null(body!.Explanation);
        Assert.Equal(0, spy.CallCount);
    }

    [Fact]
    public async Task Failing_chat_client_still_returns_the_full_comparison_with_null_explanation()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = request => RespondForTwoProducts(request, productA, productB, categoryId),
            PricingResponder = _ => (HttpStatusCode.OK, new PricingBatchResponse(
                [
                    new PricingOfferDto(productA, new PricingMoneyDto(14500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                    new PricingOfferDto(productB, new PricingMoneyDto(13500m, "UAH"), null, "InStock", DateTimeOffset.UtcNow, "seed"),
                ], [])),
            ChatClientOverride = new SpyChatClient(callsAreErrors: true),
        };
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/comparisons", new DirectComparisonRequest(
            [productA.ToString(), productB.ToString()], IncludeExplanation: true));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DirectComparisonResponse>();
        Assert.NotNull(body);
        Assert.Null(body!.Explanation);
        Assert.NotEmpty(body.Rows);
        Assert.NotEmpty(body.Criteria);
    }

    [Fact]
    public async Task Fewer_than_two_product_ids_returns_400()
    {
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/comparisons", new DirectComparisonRequest([Guid.NewGuid().ToString()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fewer_than_two_resolvable_product_ids_returns_400()
    {
        await using var factory = new AdvisorApiFactory(fixture.ConnectionString)
        {
            CatalogResponder = _ => (HttpStatusCode.NotFound, null),
        };
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/comparisons", new DirectComparisonRequest([Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static (HttpStatusCode, object?) RespondForTwoProducts(HttpRequestMessage request, Guid productA, Guid productB, Guid categoryId)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == $"/api/catalog/products/{productA}")
        {
            return (HttpStatusCode.OK, new CatalogProductDto(productA, "Galaxy S24", "Samsung", "Smartphones",
                categoryId, [new CatalogSpecificationDto("camera_mp", "50", "MP")]));
        }

        if (path == $"/api/catalog/products/{productB}")
        {
            return (HttpStatusCode.OK, new CatalogProductDto(productB, "Pixel 9", "Google", "Smartphones",
                categoryId, [new CatalogSpecificationDto("camera_mp", "48", "MP")]));
        }

        if (path == $"/api/catalog/categories/{categoryId}")
        {
            return (HttpStatusCode.OK, new CatalogCategoryDto(categoryId, "Smartphones", ["camera_mp"]));
        }

        return (HttpStatusCode.NotFound, null);
    }

    /// <summary>An IChatClient that either throws (proving zero/failed calls) or counts invocations.</summary>
    private sealed class SpyChatClient(bool callsAreErrors) : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (callsAreErrors)
            {
                throw new InvalidOperationException("Simulated chat provider failure.");
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by these tests.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

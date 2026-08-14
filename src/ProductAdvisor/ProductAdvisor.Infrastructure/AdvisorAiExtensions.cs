using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;

namespace ProductAdvisor.Infrastructure;

/// <summary>
/// Registers the Advisor's chat/LLM client purely through configuration (env vars / Aspire
/// parameters), so the provider is swappable without touching code (research.md §10). Any
/// OpenAI-API-compatible free-tier provider can be plugged in via <c>LlmProvider:Endpoint</c>.
/// </summary>
public static class AdvisorAiExtensions
{
    public static IHostApplicationBuilder AddAdvisorChatClient(this IHostApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["LlmProvider:Endpoint"];
        var apiKey = builder.Configuration["LlmProvider:ApiKey"];
        var model = builder.Configuration["LlmProvider:Model"];

        // Silently falling back to a hard-coded model produced a failure that looked like a
        // product defect rather than a configuration one: a smaller default model failed to
        // extract the category and misread "15,000 UAH" as 15 UAH, so every fully-specified
        // request came back asking for more detail. The credential has the same property — an
        // unset key turns every turn into "I didn't quite catch that". Neither is acceptable to
        // guess at in Production.
        if (builder.Environment.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "LlmProvider:ApiKey is not configured. Set the environment variable " +
                    "LlmProvider__ApiKey on this service — without it every conversation turn " +
                    "fails extraction and degrades to a generic clarification.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException(
                    "LlmProvider:Model is not configured. Set the environment variable " +
                    "LlmProvider__Model on this service — the built-in fallback is a smaller " +
                    "model that extracts requirements unreliably, which surfaces as the advisor " +
                    "asking for details the shopper already gave.");
            }
        }

        // spec.md FR-071–FR-079 (data-model.md TurnResourceBudget): these two loop-shaped limits
        // are enforced by the function-invocation middleware's own configuration rather than a
        // separate counter — see TurnResourceBudgetGuard for the empirically-verified behavior
        // (graceful stop vs. thrown exception) this relies on.
        var maxToolCalls = builder.Configuration.GetValue("TurnResourceBudget:MaxToolCallsPerTurn", 6);
        var maxConsecutiveErrors = builder.Configuration.GetValue("TurnResourceBudget:MaxConsecutiveToolErrors", 2);

        builder.Services.AddChatClient(_ =>
        {
            var options = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                options.Endpoint = new Uri(endpoint);
            }

            var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(apiKey) ? "unset" : apiKey);
            var openAiClient = new OpenAIClient(credential, options);
            return openAiClient.GetChatClient(string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model).AsIChatClient();
        })
        .UseFunctionInvocation(configure: c =>
        {
            c.MaximumIterationsPerRequest = maxToolCalls;
            c.MaximumConsecutiveErrorsPerRequest = maxConsecutiveErrors;
            // FR-069: a compute/terminal tool call must never run concurrently with another
            // tool call within the same turn.
            c.AllowConcurrentInvocation = false;
        });

        return builder;
    }

    /// <summary>
    /// Registers the embedding generator backing store-knowledge retrieval (002 research.md §7).
    /// Reuses the same provider endpoint/credential as the chat client — an embedding model is
    /// simply a different model on the same OpenAI-compatible provider — so no second provider
    /// account or client stack is introduced. The model name is its own setting because chat and
    /// embedding models are almost never the same model.
    /// </summary>
    public static IHostApplicationBuilder AddAdvisorEmbeddingGenerator(this IHostApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["LlmProvider:Endpoint"];
        var apiKey = builder.Configuration["LlmProvider:ApiKey"];
        var embeddingModel = builder.Configuration["LlmProvider:EmbeddingModel"];

        builder.Services.AddEmbeddingGenerator(_ =>
        {
            var options = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                options.Endpoint = new Uri(endpoint);
            }

            var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(apiKey) ? "unset" : apiKey);
            var openAiClient = new OpenAIClient(credential, options);
            return openAiClient
                .GetEmbeddingClient(string.IsNullOrWhiteSpace(embeddingModel) ? "text-embedding-3-small" : embeddingModel)
                .AsIEmbeddingGenerator();
        });

        return builder;
    }
}

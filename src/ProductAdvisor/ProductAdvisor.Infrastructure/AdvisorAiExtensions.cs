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
}

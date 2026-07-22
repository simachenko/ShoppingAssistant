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
        .UseFunctionInvocation();

        return builder;
    }
}

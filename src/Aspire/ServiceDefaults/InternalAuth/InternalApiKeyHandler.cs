using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Attaches the shared internal service credential to every outbound HttpClient call (FR-029,
/// research.md §18) — registered as a default handler for every HttpClient in
/// <see cref="Extensions.AddServiceDefaults"/>, mirroring <see cref="CorrelationIdHandler"/>.
/// Every HttpClient in this solution calls another internal service (Catalog/Pricing/Advisor/
/// Gateway) — none call third parties directly through the DI-managed HttpClientFactory — so
/// applying this uniformly never leaks the key to an external party (the LLM provider client is
/// a separate SDK client, not registered through this pipeline).
/// </summary>
public sealed class InternalApiKeyHandler(IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = configuration["InternalApiKey"];
        if (!string.IsNullOrEmpty(key) && !request.Headers.Contains(InternalApiKeyMiddleware.HeaderName))
        {
            request.Headers.Add(InternalApiKeyMiddleware.HeaderName, key);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

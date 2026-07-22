using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Forwards the current request's correlation id onto every outbound HttpClient call, so it
/// propagates across every service-to-service hop (Gateway → Advisor → Catalog/Pricing).
/// Registered as a default handler for every HttpClient in <see cref="Extensions.AddServiceDefaults"/>.
/// </summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.HttpContextItemKey] as string;

        if (!string.IsNullOrWhiteSpace(correlationId) &&
            !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Generates an <see cref="HeaderName"/> if the inbound request doesn't already carry one,
/// stores it for the duration of the request, attaches it to the log scope, and echoes it back
/// on the response — the human-readable correlation id that complements the W3C traceparent
/// span id (research.md §7 / constitution Principle VI).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string HttpContextItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("n");

        context.Items[HttpContextItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // The structured-message-template overload (not a raw Dictionary) so the scope actually
        // renders as "CorrelationId:<value>" in log output — a Dictionary's default ToString()
        // just prints its type name, silently defeating the whole point of this scope.
#pragma warning disable CA1848 // Intentional: once per HTTP request, not a hot logging path — a LoggerMessage delegate would add ceremony with no measurable benefit here.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await next(context);
        }
#pragma warning restore CA1848
    }
}

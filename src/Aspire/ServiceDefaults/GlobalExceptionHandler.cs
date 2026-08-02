using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Logs every unhandled exception once, tagged with the request's correlation id, and returns a
/// clean <c>application/problem+json</c> body instead of a raw stack trace or the framework's
/// default developer exception page in Production (FR-027/FR-032, research.md §16). Registered
/// for the four API services (Catalog, Pricing, Advisor, Gateway) — <c>WebApp.Blazor</c> already
/// has its own equivalent via <c>UseExceptionHandler("/Error")</c>.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdMiddleware.HttpContextItemKey] as string;

        // BadHttpRequestException (e.g. malformed JSON body, invalid enum value during model
        // binding) already carries the framework's intended client-error status code (typically
        // 400) — preserve it instead of collapsing every exception to 500, and log it as a
        // client mistake, not a server failure.
        var statusCode = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception handling {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Client error handling {Method} {Path}: {StatusCode}",
                httpContext.Request.Method, httpContext.Request.Path, statusCode);
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = statusCode >= StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : "The request could not be processed.",
            status = statusCode,
            correlationId,
        }, cancellationToken);

        return true;
    }
}

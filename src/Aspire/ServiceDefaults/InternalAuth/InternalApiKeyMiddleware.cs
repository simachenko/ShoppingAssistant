using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Rejects any request that doesn't present the shared internal service credential (FR-029,
/// research.md §18) — applied only to services that are never called by a browser (Catalog,
/// Pricing, Advisor), never to Gateway or the WebApp, which are protected by Google OAuth
/// instead (research.md §17). Health/liveness checks are exempt so Render's own prober (which
/// doesn't know this key) can still determine the service is up.
/// </summary>
public sealed class InternalApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string HeaderName = "X-Internal-Api-Key";

    private static readonly string[] ExemptPathPrefixes = ["/health", "/alive"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            await next(context);
            return;
        }

        var expectedKey = configuration["InternalApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
        {
            // Misconfigured deployment — fail closed rather than silently accepting any caller.
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        var providedKey = context.Request.Headers.TryGetValue(HeaderName, out var values) ? values.ToString() : null;
        if (!string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}

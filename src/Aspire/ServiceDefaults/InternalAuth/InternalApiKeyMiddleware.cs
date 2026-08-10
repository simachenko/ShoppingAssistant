using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Rejects any request that doesn't present the shared internal service credential (FR-029,
/// research.md §18) — applied only to services that are never called by a browser (Catalog,
/// Pricing, Advisor), never to Gateway or the WebApp, which are protected by Google OAuth
/// instead (research.md §17). Health/liveness checks are exempt so Render's own prober (which
/// doesn't know this key) can still determine the service is up.
/// </summary>
public sealed partial class InternalApiKeyMiddleware(
    RequestDelegate next, IConfiguration configuration, IHostEnvironment environment, ILogger<InternalApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-Internal-Api-Key";

    /// <summary>
    /// Matches `docker-compose.yml`'s `INTERNAL_API_KEY` fallback exactly — the actual local
    /// value, not a placeholder invented for this check, so a config accidentally carried from a
    /// developer's machine (or an un-set `sync: false` Render secret silently resolving to this
    /// same string some other way) into a Production deployment is caught here rather than
    /// merely hoped never to happen (FR-127, research.md §18 — this exact scenario is the real
    /// incident that motivated this middleware's original credential check).
    /// </summary>
    public const string LocalDevelopmentPlaceholder = "dev-internal-api-key";

    /// <summary>FR-128: an old credential remains valid for a configured overlap window during
    /// rotation — set this alongside a new <c>InternalApiKey</c>, then remove it once every
    /// caller has been updated to the new value. Never itself subject to the
    /// production-placeholder check below (a genuine previous production secret is not the dev
    /// placeholder).</summary>
    private const string PreviousKeyConfigName = "InternalApiKeyPrevious";

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
            // Every real endpoint 500s while /health and /alive stay green, so this must be
            // logged loudly: without a log line here, the failure is otherwise invisible in both
            // application logs and traces (no exception is ever thrown).
            LogKeyNotConfigured(context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        if (environment.IsProduction() && string.Equals(expectedKey, LocalDevelopmentPlaceholder, StringComparison.Ordinal))
        {
            // FR-127: never merely trust that the deployment config was set correctly — a
            // Production environment carrying the known local-development value fails closed the
            // same way an unset key does, rather than accepting every caller that happens to know
            // a value that was only ever meant for a developer's own machine.
            LogProductionPlaceholderDetected(context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        var providedKey = context.Request.Headers.TryGetValue(HeaderName, out var values) ? values.ToString() : null;
        var previousKey = configuration[PreviousKeyConfigName];

        // FR-126: constant-time comparison — string.Equals/== short-circuits on the first
        // mismatched character, which a network-timing attacker can exploit to recover the
        // secret one byte at a time. Checked against both the current and (if configured, for
        // rotation) previous key, each independently constant-time; failing the first check
        // never skips evaluating the second based on how quickly it failed.
        var matchesCurrent = ConstantTimeEquals(providedKey, expectedKey);
        var matchesPrevious = !string.IsNullOrEmpty(previousKey) && ConstantTimeEquals(providedKey, previousKey);
        if (!matchesCurrent && !matchesPrevious)
        {
            LogRejectedCredential(context.Request.Method, context.Request.Path, HeaderName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Rejecting {Method} {Path}: InternalApiKey is not configured on this service — every non-exempt request will 500 until it's set.")]
    private partial void LogKeyNotConfigured(string method, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rejecting {Method} {Path}: InternalApiKey is set to the local-development placeholder value while running in Production.")]
    private partial void LogProductionPlaceholderDetected(string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejecting {Method} {Path}: missing or incorrect {Header}.")]
    private partial void LogRejectedCredential(string method, string path, string header);

    private static bool ConstantTimeEquals(string? provided, string expected)
    {
        if (provided is null)
        {
            return false;
        }

        // FixedTimeEquals itself returns false immediately when lengths differ (a length check,
        // not a content comparison — it leaks nothing about *where* a mismatch occurs) and
        // otherwise compares every byte in constant time regardless of where the first
        // difference falls.
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }
}

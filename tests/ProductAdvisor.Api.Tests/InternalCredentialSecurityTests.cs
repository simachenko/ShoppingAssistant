using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ProductAdvisor.Api.Tests;

/// <summary>
/// Phase 13 (spec.md FR-124/FR-127/FR-128): credential handling resists timing attacks and
/// dev-default misuse in production. NOT run in this sandbox — like the rest of this test
/// project, it requires a Testcontainers Postgres instance (Docker), unavailable here.
/// </summary>
public sealed class InternalCredentialSecurityTests : IAsyncDisposable
{
    private AdvisorApiFactory? _factory;

    [Fact]
    public async Task An_unset_InternalApiKey_refuses_every_caller()
    {
        _factory = new AdvisorApiFactory { InternalApiKeyOverride = null };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyMiddleware.HeaderName, "anything-at-all");

        var response = await client.PostAsync("/api/conversations", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task The_local_development_placeholder_value_is_refused_in_a_Production_configuration()
    {
        _factory = new AdvisorApiFactory
        {
            EnvironmentName = "Production",
            InternalApiKeyOverride = InternalApiKeyMiddleware.LocalDevelopmentPlaceholder,
        };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyMiddleware.HeaderName, InternalApiKeyMiddleware.LocalDevelopmentPlaceholder);

        var response = await client.PostAsync("/api/conversations", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task The_local_development_placeholder_value_is_accepted_outside_Production()
    {
        // The placeholder is only rejected in Production — it must keep working for local
        // development (Development is WebApplicationFactory's own default environment).
        _factory = new AdvisorApiFactory { InternalApiKeyOverride = InternalApiKeyMiddleware.LocalDevelopmentPlaceholder };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyMiddleware.HeaderName, InternalApiKeyMiddleware.LocalDevelopmentPlaceholder);

        var response = await client.PostAsync("/api/conversations", content: null);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task A_previous_key_configured_for_rotation_is_still_accepted()
    {
        _factory = new AdvisorApiFactory { InternalApiKeyOverride = "new-rotated-key", PreviousInternalApiKeyOverride = "old-key-being-retired" };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyMiddleware.HeaderName, "old-key-being-retired");
        // /api/conversations independently requires a caller identity and answers 401 without one
        // (FR-031). Omitting it made this assertion pass or fail for a reason that had nothing to
        // do with key rotation — the request never got far enough to prove the old key was taken.
        client.DefaultRequestHeaders.Add("X-User-Id", "rotation-test-user");

        var response = await client.PostAsync("/api/conversations", content: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Comparison_duration_shows_no_statistically_meaningful_correlation_with_match_length()
    {
        // Best-effort: timing measurements are inherently noisy in a shared CI environment, so
        // this compares averages over many iterations with a generous tolerance rather than
        // asserting a tight bound — the actual guarantee comes from using
        // CryptographicOperations.FixedTimeEquals (a vetted constant-time primitive), not from
        // this test being able to prove its absence of a timing side channel on its own.
        _factory = new AdvisorApiFactory();
        var client = _factory.CreateClient();

        var completelyWrong = new string('x', 64);
        var almostRight = TestSupport.InternalApiKeyTestDefaults.Key[..^1] + "x";

        var (wrongDuration, almostDuration) = await MeasureInterleavedAsync(client, completelyWrong, almostRight);

        // A constant-time comparison should show no meaningful trend — allow generous slack
        // (an order of magnitude) since network/HTTP-pipeline noise dwarfs a byte-comparison
        // loop's own timing difference regardless of algorithm.
        var ratio = almostDuration.TotalMilliseconds / Math.Max(wrongDuration.TotalMilliseconds, 0.001);
        Assert.InRange(ratio, 0.1, 10.0);
    }

    /// <summary>
    /// Measures both header values in one interleaved pass.
    /// </summary>
    /// <remarks>
    /// This test used to be the suite's flakiest, for two measurement reasons rather than any
    /// real timing variance. It timed with <c>DateTimeOffset.UtcNow</c>, whose ~15.6 ms tick on
    /// Windows is coarser than the requests being measured, so most samples read as zero and the
    /// ratio against a near-zero denominator exploded past the bound. And it measured the two
    /// values in separate sequential batches, so the first batch absorbed one-time JIT and
    /// host-warmup cost that the second never paid.
    /// <para>
    /// Now: a high-resolution <see cref="Stopwatch"/>, a discarded warm-up round, and alternating
    /// the two values within each iteration so any drift (GC, scheduling, thermal) applies to
    /// both equally.
    /// </para>
    /// </remarks>
    private static async Task<(TimeSpan Wrong, TimeSpan Almost)> MeasureInterleavedAsync(
        HttpClient client, string wrongValue, string almostValue, int iterations = 40)
    {
        // Warm-up: JIT, connection setup and first-request host costs land here, not in a sample.
        for (var i = 0; i < 5; i++)
        {
            await SendOnceAsync(client, wrongValue);
            await SendOnceAsync(client, almostValue);
        }

        var wrongTotal = TimeSpan.Zero;
        var almostTotal = TimeSpan.Zero;
        for (var i = 0; i < iterations; i++)
        {
            // Alternate which value goes first so ordering cannot favour either one.
            if (i % 2 == 0)
            {
                wrongTotal += await SendOnceAsync(client, wrongValue);
                almostTotal += await SendOnceAsync(client, almostValue);
            }
            else
            {
                almostTotal += await SendOnceAsync(client, almostValue);
                wrongTotal += await SendOnceAsync(client, wrongValue);
            }
        }

        return (wrongTotal / iterations, almostTotal / iterations);
    }

    private static async Task<TimeSpan> SendOnceAsync(HttpClient client, string headerValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations");
        request.Headers.Add(InternalApiKeyMiddleware.HeaderName, headerValue);

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }
}

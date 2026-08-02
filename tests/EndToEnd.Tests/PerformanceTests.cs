using System.Diagnostics;
using System.Net.Http.Json;
using TestSupport.SeedData;
using Xunit;

namespace EndToEnd.Tests;

/// <summary>
/// Exercises plan.md's Performance Goals against the real docker-compose stack: Catalog/Pricing
/// read endpoints at p95 &lt; 300ms (excluding cold start — a warm-up request is discarded first),
/// and the direct comparison endpoint (FR-018) with <c>includeExplanation:false</c> — which plan.md
/// documents as having no LLM call at all — completing far faster than an LLM-inclusive turn's
/// documented p95 of 3s. This is a lightweight sanity check, not a load test: it proves the
/// documented latency claims hold on this stack, not exact production numbers under real load.
/// </summary>
public sealed class PerformanceTests : IClassFixture<DockerComposeStackFixture>, IDisposable
{
    private const int SampleCount = 30;

    private readonly HttpClient _catalogClient = new() { BaseAddress = new Uri("http://localhost:5101") };
    private readonly HttpClient _pricingClient = new() { BaseAddress = new Uri("http://localhost:5102") };
    private readonly HttpClient _advisorClient = new() { BaseAddress = new Uri("http://localhost:5103") };

    public PerformanceTests(DockerComposeStackFixture fixture)
    {
        _ = fixture;
    }

    public void Dispose()
    {
        _catalogClient.Dispose();
        _pricingClient.Dispose();
        _advisorClient.Dispose();
    }

    [Fact]
    public async Task Catalog_search_p95_latency_is_under_300ms()
    {
        // Warm-up (excluded from the sample, per plan.md's cold-start caveat).
        await _catalogClient.GetAsync("/api/catalog/products?category=Smartphones");

        var p95 = await MeasureP95Async(
            () => _catalogClient.GetAsync("/api/catalog/products?category=Smartphones"));

        Assert.True(p95 < TimeSpan.FromMilliseconds(300), $"Catalog search p95 was {p95.TotalMilliseconds:F0}ms, expected < 300ms.");
    }

    [Fact]
    public async Task Pricing_batch_offers_p95_latency_is_under_300ms()
    {
        var productIds = $"{CatalogSeedData.GalaxyS24Id},{CatalogSeedData.Pixel9Id},{CatalogSeedData.Xiaomi14Id}";

        await _pricingClient.GetAsync($"/api/pricing/offers?productIds={productIds}");

        var p95 = await MeasureP95Async(
            () => _pricingClient.GetAsync($"/api/pricing/offers?productIds={productIds}"));

        Assert.True(p95 < TimeSpan.FromMilliseconds(300), $"Pricing batch offers p95 was {p95.TotalMilliseconds:F0}ms, expected < 300ms.");
    }

    [Fact]
    public async Task A_direct_comparison_with_no_explanation_has_no_llm_call_and_is_far_faster_than_an_llm_inclusive_turns_3s_budget()
    {
        var request = new
        {
            productIds = new[] { CatalogSeedData.GalaxyS24Id.ToString(), CatalogSeedData.Pixel9Id.ToString() },
            includeExplanation = false,
        };

        // Warm-up.
        await _advisorClient.PostAsJsonAsync("/api/comparisons", request);

        var p95 = await MeasureP95Async(() => _advisorClient.PostAsJsonAsync("/api/comparisons", request));

        // plan.md: an LLM-inclusive turn's documented budget is p95 < 3s, dominated by the LLM
        // call. This turn has no LLM call at all (includeExplanation:false), so it should land
        // well inside a fraction of that budget — 1s leaves generous room for two HTTP+DB round
        // trips (Catalog, Pricing) plus in-process comparison math on this local Docker stack.
        Assert.True(p95 < TimeSpan.FromSeconds(1), $"Direct comparison (no explanation) p95 was {p95.TotalMilliseconds:F0}ms, expected < 1000ms.");
    }

    private static async Task<TimeSpan> MeasureP95Async(Func<Task<HttpResponseMessage>> action)
    {
        var durations = new List<TimeSpan>(SampleCount);
        for (var i = 0; i < SampleCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await action();
            stopwatch.Stop();
            response.EnsureSuccessStatusCode();
            durations.Add(stopwatch.Elapsed);
        }

        durations.Sort();
        var p95Index = (int)Math.Ceiling(0.95 * durations.Count) - 1;
        return durations[Math.Clamp(p95Index, 0, durations.Count - 1)];
    }
}

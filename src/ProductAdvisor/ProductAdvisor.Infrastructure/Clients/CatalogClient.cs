using System.Net;
using System.Net.Http.Json;

namespace ProductAdvisor.Infrastructure.Clients;

/// <summary>
/// Thin HTTP client to the Catalog service. Registered with the standard resilience handler
/// (timeout/retry/circuit-breaker) via ServiceDefaults' <c>ConfigureHttpClientDefaults</c> —
/// this class does no filtering/scoring, it only fetches (research.md §1, §6).
/// </summary>
public sealed class CatalogClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<CatalogProductDto>> SearchProductsAsync(
        string category, string? query, CancellationToken cancellationToken)
    {
        var url = $"/api/catalog/products?category={Uri.EscapeDataString(category)}&pageSize=50";
        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"&q={Uri.EscapeDataString(query)}";
        }

        var result = await httpClient.GetFromJsonAsync<CatalogSearchResponse>(url, cancellationToken);
        return result?.Items ?? [];
    }

    public async Task<CatalogProductDto?> GetProductDetailAsync(Guid productId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/catalog/products/{productId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProductDto>(cancellationToken);
    }

    public async Task<CatalogCategoryDto?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/catalog/categories/{categoryId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogCategoryDto>(cancellationToken);
    }
}

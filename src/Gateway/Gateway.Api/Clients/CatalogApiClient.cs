using System.Net;
using System.Net.Http.Json;

namespace Gateway.Api.Clients;

/// <summary>Thrown when Catalog rejects a search request (e.g. an unrecognized characteristic
/// operator) so the Gateway endpoint can mirror the same 400 rather than a generic failure.</summary>
public sealed class CatalogBadRequestException(string message) : Exception(message);

/// <summary>Thin HTTP client to the Product Catalog service — fetch only, no computation.</summary>
public sealed class CatalogApiClient(HttpClient httpClient)
{
    public async Task<CatalogSearchResponse> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/api/catalog/products/search", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new CatalogBadRequestException(errorMessage);
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CatalogSearchResponse>(cancellationToken);
        return result ?? new CatalogSearchResponse([], request.Page, request.PageSize, 0);
    }

    public async Task<CatalogProductDetailDto?> GetProductDetailAsync(Guid productId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/catalog/products/{productId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProductDetailDto>(cancellationToken);
    }

    /// <summary>Used only by <c>GET /api/system-status</c> (FR-033, research.md §19) — reuses the
    /// service's own liveness check rather than a second "are you up" mechanism.</summary>
    public Task<bool> IsAliveAsync(CancellationToken cancellationToken) =>
        ServiceLivenessProbe.WaitUntilAliveAsync(httpClient, cancellationToken);
}

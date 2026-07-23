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
}

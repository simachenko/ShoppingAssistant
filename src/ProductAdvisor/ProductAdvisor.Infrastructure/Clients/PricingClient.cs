using System.Net;
using System.Net.Http.Json;

namespace ProductAdvisor.Infrastructure.Clients;

/// <summary>Thin HTTP client to the Pricing and Availability service — fetch only, no computation.</summary>
public sealed class PricingClient(HttpClient httpClient)
{
    public async Task<PricingOfferDto?> GetOfferAsync(Guid productId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/pricing/offers/{productId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PricingOfferDto>(cancellationToken);
    }

    public async Task<PricingBatchResponse> GetOffersAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        var idsParam = string.Join(',', productIds);
        var response = await httpClient.GetAsync($"/api/pricing/offers?productIds={idsParam}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PricingBatchResponse>(cancellationToken);
        return result ?? new PricingBatchResponse([], productIds.ToList());
    }
}

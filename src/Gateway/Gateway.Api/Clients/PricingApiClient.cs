using System.Net.Http.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Gateway.Api.Clients;

/// <summary>Thin HTTP client to the Pricing and Availability service — fetch only, no computation.</summary>
public sealed class PricingApiClient(HttpClient httpClient)
{
    public async Task<PricingBatchResponse> GetOffersAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new PricingBatchResponse([], []);
        }

        try
        {
            var idsParam = string.Join(',', productIds);
            var response = await httpClient.GetAsync($"/api/pricing/offers?productIds={idsParam}", cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<PricingBatchResponse>(cancellationToken);
            return result ?? new PricingBatchResponse([], productIds.ToList());
        }
        catch (Exception ex) when (IsPricingUnreachable(ex))
        {
            // Pricing entirely down — degrade to unverified prices rather than failing the whole
            // search (constitution Principle V: honest partial response over total failure).
            return new PricingBatchResponse([], productIds.ToList());
        }
    }

    private static bool IsPricingUnreachable(Exception ex) =>
        ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException;
}

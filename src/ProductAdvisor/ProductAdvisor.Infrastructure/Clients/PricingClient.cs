using System.Net;
using System.Net.Http.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ProductAdvisor.Infrastructure.Clients;

/// <summary>Thin HTTP client to the Pricing and Availability service — fetch only, no computation.</summary>
public sealed class PricingClient(HttpClient httpClient)
{
    public async Task<PricingOfferDto?> GetOfferAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync($"/api/pricing/offers/{productId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PricingOfferDto>(cancellationToken);
        }
        catch (Exception ex) when (IsPricingUnreachable(ex))
        {
            // Pricing entirely unreachable (DNS/connection failure, or retries exhausted) rather
            // than a per-product 404 — degrade to unverified instead of failing the tool call
            // (constitution Principle V).
            return null;
        }
    }

    public async Task<PricingBatchResponse> GetOffersAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
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
            return new PricingBatchResponse([], productIds.ToList());
        }
    }

    /// <summary>
    /// True for total-outage failures (connection refused, DNS failure, exhausted retries,
    /// open circuit breaker) as opposed to a normal HTTP error status from a reachable service.
    /// </summary>
    private static bool IsPricingUnreachable(Exception ex) =>
        ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException;
}

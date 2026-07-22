using System.Net;
using System.Net.Http.Json;
using PricingAvailability.Application.Contracts;
using TestSupport.SeedData;
using Xunit;

namespace PricingAvailability.Api.Tests;

public sealed class BatchOffersContractTests(PricingApiTestFixture fixture) : IClassFixture<PricingApiTestFixture>
{
    [Fact]
    public async Task Single_offer_lookup_returns_the_seeded_offer()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/pricing/offers/{CatalogSeedData.GalaxyS24Id}");

        response.EnsureSuccessStatusCode();
        var offer = await response.Content.ReadFromJsonAsync<OfferDto>();
        Assert.NotNull(offer);
        Assert.Equal(14500m, offer!.Price.Amount);
        Assert.Equal("UAH", offer.Price.Currency);
        Assert.Equal("InStock", offer.Availability);
    }

    [Fact]
    public async Task Single_offer_lookup_for_unknown_product_returns_404()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/pricing/offers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Batch_lookup_returns_partial_results_when_some_ids_are_unknown()
    {
        var client = fixture.Factory.CreateClient();
        var unknownId = Guid.NewGuid();

        var response = await client.GetAsync(
            $"/api/pricing/offers?productIds={CatalogSeedData.GalaxyS24Id},{unknownId}");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BatchOffersResult>();
        Assert.NotNull(result);
        Assert.Single(result!.Offers);
        Assert.Equal(CatalogSeedData.GalaxyS24Id, result.Offers[0].ProductId);
        Assert.Contains(unknownId, result.NotFound);
    }

    [Fact]
    public async Task Batch_lookup_with_no_productIds_returns_400()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/pricing/offers?productIds=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_availability_is_distinguishable_from_missing_offer()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/pricing/offers/{CatalogSeedData.Xps13Id}");

        response.EnsureSuccessStatusCode();
        var offer = await response.Content.ReadFromJsonAsync<OfferDto>();
        Assert.Equal("Unknown", offer!.Availability);
    }
}

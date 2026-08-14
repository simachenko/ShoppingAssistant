using Xunit;

namespace PricingAvailability.Api.Tests;

/// <summary>
/// Puts every Pricing contract-test class in one xUnit collection, sharing a single
/// <see cref="PricingApiTestFixture"/> — see <c>ProductCatalog.Api.Tests.CatalogApiCollection</c>
/// for the full explanation.
/// </summary>
/// <remarks>
/// Same defect as the Catalog suite: per-class fixtures ran in parallel and raced on the
/// process-global <c>ConnectionStrings__pricingdb</c> environment variable, so a host could be
/// pointed at another class's container. It showed up here as intermittent duplicate-key failures
/// while seeding offers.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PricingApiCollection : ICollectionFixture<PricingApiTestFixture>
{
    public const string Name = "pricing-api";
}

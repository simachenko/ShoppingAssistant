using PricingAvailability.Domain;

namespace PricingAvailability.Domain.Tests;

public class OfferTests
{
    private static Offer CreateOffer(StockStatus availability = StockStatus.Unknown) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new Money(14999m, "UAH"), DateTimeOffset.UtcNow, "seed", availability);

    [Fact]
    public void Availability_defaults_to_Unknown_when_not_specified()
    {
        var offer = new Offer(Guid.NewGuid(), Guid.NewGuid(), new Money(100m, "UAH"), DateTimeOffset.UtcNow, "seed");

        Assert.Equal(StockStatus.Unknown, offer.Availability);
    }

    [Fact]
    public void Default_enum_value_is_Unknown_not_InStock()
    {
        // Guards against ever silently defaulting to InStock (FR-005) if a caller forgets
        // to set Availability explicitly.
        Assert.Equal(StockStatus.Unknown, default(StockStatus));
    }

    [Fact]
    public void Constructor_throws_when_offer_id_is_empty()
    {
        Assert.Throws<ArgumentException>(
            () => new Offer(Guid.Empty, Guid.NewGuid(), new Money(100m, "UAH"), DateTimeOffset.UtcNow, "seed"));
    }

    [Fact]
    public void Constructor_throws_when_source_is_empty()
    {
        Assert.Throws<ArgumentException>(
            () => new Offer(Guid.NewGuid(), Guid.NewGuid(), new Money(100m, "UAH"), DateTimeOffset.UtcNow, ""));
    }

    [Fact]
    public void UpdatePrice_replaces_price_and_freshness_timestamp()
    {
        var offer = CreateOffer();
        var newAsOf = DateTimeOffset.UtcNow.AddMinutes(5);

        offer.UpdatePrice(new Money(13999m, "UAH"), newAsOf);

        Assert.Equal(13999m, offer.Price.Amount);
        Assert.Equal(newAsOf, offer.AsOf);
    }

    [Fact]
    public void UpdateAvailability_replaces_status_and_freshness_timestamp()
    {
        var offer = CreateOffer();
        var newAsOf = DateTimeOffset.UtcNow.AddMinutes(5);

        offer.UpdateAvailability(StockStatus.OutOfStock, newAsOf);

        Assert.Equal(StockStatus.OutOfStock, offer.Availability);
        Assert.Equal(newAsOf, offer.AsOf);
    }

    [Fact]
    public void ApplyDiscount_then_ClearDiscount_round_trips()
    {
        var offer = CreateOffer();
        offer.ApplyDiscount(new Discount(10, DateTimeOffset.UtcNow.AddDays(7)));
        Assert.NotNull(offer.Discount);

        offer.ClearDiscount();

        Assert.Null(offer.Discount);
    }
}

public class MoneyTests
{
    [Fact]
    public void Constructor_throws_for_negative_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1m, "UAH"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Constructor_throws_for_invalid_currency_code(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, currency));
    }

    [Fact]
    public void Constructor_normalizes_currency_to_uppercase()
    {
        var money = new Money(10m, "uah");

        Assert.Equal("UAH", money.Currency);
    }
}

public class DiscountTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Constructor_throws_when_percent_off_out_of_range(decimal percentOff)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Discount(percentOff));
    }
}

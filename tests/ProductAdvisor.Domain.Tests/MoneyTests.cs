using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Domain.Tests;

/// <summary>
/// Phase 12 (spec.md FR-108): a negative amount or an unrecognized currency from untrusted
/// (model-extracted) input must never crash the turn via the throwing constructor — <see cref="Money.TryCreate"/>
/// is the non-throwing counterpart used specifically for that content.
/// </summary>
public class MoneyTests
{
    [Fact]
    public void TryCreate_succeeds_for_a_non_negative_amount_and_a_known_currency()
    {
        var succeeded = Money.TryCreate(15000m, "UAH", out var money);

        Assert.True(succeeded);
        Assert.NotNull(money);
        Assert.Equal(15000m, money!.Amount);
        Assert.Equal("UAH", money.Currency);
    }

    [Fact]
    public void TryCreate_fails_for_a_negative_amount_instead_of_throwing()
    {
        var succeeded = Money.TryCreate(-500m, "UAH", out var money);

        Assert.False(succeeded);
        Assert.Null(money);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("US")]
    [InlineData(null)]
    [InlineData("")]
    public void TryCreate_fails_for_an_unrecognized_or_malformed_currency_instead_of_throwing(string? currency)
    {
        var succeeded = Money.TryCreate(15000m, currency, out var money);

        Assert.False(succeeded);
        Assert.Null(money);
    }

    [Fact]
    public void TryCreate_is_case_insensitive_for_a_known_currency()
    {
        var succeeded = Money.TryCreate(15000m, "uah", out var money);

        Assert.True(succeeded);
        Assert.Equal("UAH", money!.Currency);
    }

    [Fact]
    public void IsKnownCurrencyCode_reports_common_supported_currencies_as_known()
    {
        Assert.True(Money.IsKnownCurrencyCode("USD"));
        Assert.True(Money.IsKnownCurrencyCode("UAH"));
        Assert.False(Money.IsKnownCurrencyCode("XYZ"));
    }
}

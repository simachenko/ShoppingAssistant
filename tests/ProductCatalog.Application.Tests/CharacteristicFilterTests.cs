using ProductCatalog.Application.Contracts;
using ProductCatalog.Domain;

namespace ProductCatalog.Application.Tests;

public class CharacteristicFilterTests
{
    private static Product ProductWithSpecs(params (string Key, string Value, string? Unit)[] specs)
    {
        var product = new Product(Guid.NewGuid(), "Test Product", Guid.NewGuid(), Guid.NewGuid(), "A test product.");
        foreach (var (key, value, unit) in specs)
        {
            product.AddSpecification(new Specification(key, value, unit));
        }

        return product;
    }

    [Fact]
    public void Equals_operator_matches_numeric_values_numerically()
    {
        var product = ProductWithSpecs(("storage_gb", "256", "GB"));
        var filter = new CharacteristicFilter("storage_gb", CharacteristicFilterOperator.Equals, "256");

        Assert.True(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void Equals_operator_matches_non_numeric_values_case_insensitively()
    {
        var product = ProductWithSpecs(("noise_cancelling", "Yes", null));
        var filter = new CharacteristicFilter("noise_cancelling", CharacteristicFilterOperator.Equals, "yes");

        Assert.True(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void GreaterThanOrEqual_matches_when_spec_value_meets_the_threshold()
    {
        var product = ProductWithSpecs(("camera_mp", "50", "MP"));
        var filter = new CharacteristicFilter("camera_mp", CharacteristicFilterOperator.GreaterThanOrEqual, "48");

        Assert.True(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void GreaterThanOrEqual_fails_when_spec_value_is_below_the_threshold()
    {
        var product = ProductWithSpecs(("camera_mp", "12", "MP"));
        var filter = new CharacteristicFilter("camera_mp", CharacteristicFilterOperator.GreaterThanOrEqual, "48");

        Assert.False(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void LessThanOrEqual_matches_when_spec_value_is_at_or_below_the_threshold()
    {
        var product = ProductWithSpecs(("battery_mah", "3349", "mAh"));
        var filter = new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.LessThanOrEqual, "4000");

        Assert.True(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void Between_matches_when_spec_value_falls_within_the_range()
    {
        var product = ProductWithSpecs(("battery_mah", "4000", "mAh"));
        var filter = new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.Between, "3500", "4500");

        Assert.True(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void Between_fails_when_spec_value_falls_outside_the_range()
    {
        var product = ProductWithSpecs(("battery_mah", "5000", "mAh"));
        var filter = new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.Between, "3500", "4500");

        Assert.False(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void Between_fails_when_valueTo_is_missing()
    {
        var product = ProductWithSpecs(("battery_mah", "4000", "mAh"));
        var filter = new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.Between, "3500");

        Assert.False(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void Unknown_attribute_key_yields_zero_matches_rather_than_being_ignored()
    {
        var product = ProductWithSpecs(("camera_mp", "50", "MP"));
        var filter = new CharacteristicFilter("waterproof_rating", CharacteristicFilterOperator.Equals, "IP68");

        Assert.False(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void Ordinal_operator_fails_when_spec_value_is_not_numeric()
    {
        var product = ProductWithSpecs(("cpu", "Intel Core i7", null));
        var filter = new CharacteristicFilter("cpu", CharacteristicFilterOperator.GreaterThanOrEqual, "5");

        Assert.False(CharacteristicFilterMatcher.MatchesAll(product, [filter]));
    }

    [Fact]
    public void All_filters_must_match_for_the_product_to_match()
    {
        var product = ProductWithSpecs(("camera_mp", "50", "MP"), ("battery_mah", "4000", "mAh"));
        var filters = new[]
        {
            new CharacteristicFilter("camera_mp", CharacteristicFilterOperator.GreaterThanOrEqual, "48"),
            new CharacteristicFilter("battery_mah", CharacteristicFilterOperator.GreaterThanOrEqual, "4500"),
        };

        Assert.False(CharacteristicFilterMatcher.MatchesAll(product, filters));
    }
}

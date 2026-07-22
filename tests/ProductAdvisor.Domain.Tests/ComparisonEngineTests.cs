using ProductAdvisor.Domain;

namespace ProductAdvisor.Domain.Tests;

public class ComparisonEngineTests
{
    private static readonly string[] SmartphoneAttributes = ["camera_mp", "battery_mah"];

    private static ProductCandidate Candidate(
        string name, decimal? price, string currency = "UAH", bool priceVerified = true,
        bool availabilityVerified = true, params (string Key, string Value, string? Unit)[] specs) =>
        new()
        {
            ProductId = Guid.NewGuid(),
            Name = name,
            Price = price is null ? null : new Money(price.Value, currency),
            PriceVerified = priceVerified,
            Availability = availabilityVerified ? StockStatus.InStock : null,
            AvailabilityVerified = availabilityVerified,
            Specifications = specs.Select(s => new Specification(s.Key, s.Value, s.Unit)).ToList(),
        };

    [Fact]
    public void Compare_throws_when_fewer_than_two_candidates()
    {
        var single = Candidate("Only Phone", 10000m);

        Assert.Throws<ArgumentException>(() => ComparisonEngine.Compare([single], SmartphoneAttributes));
    }

    [Fact]
    public void Criteria_is_price_then_the_comparable_attributes_then_availability_in_order()
    {
        var a = Candidate("Phone A", 10000m);
        var b = Candidate("Phone B", 12000m);

        var comparison = ComparisonEngine.Compare([a, b], SmartphoneAttributes);

        Assert.Equal(["price", "camera_mp", "battery_mah", "availability"], comparison.Criteria);
    }

    [Fact]
    public void Every_row_uses_the_identical_criteria_set()
    {
        var a = Candidate("Phone A", 10000m, specs: [("camera_mp", "50", "MP")]);
        var b = Candidate("Phone B", 12000m);

        var comparison = ComparisonEngine.Compare([a, b], SmartphoneAttributes);

        Assert.All(comparison.Rows, row => Assert.Equal(comparison.Criteria, row.ValuesByCriterion.Keys));
    }

    [Fact]
    public void Cheapest_candidate_is_marked_cheapest_and_others_get_a_positive_delta()
    {
        var cheap = Candidate("Cheap Phone", 10000m);
        var expensive = Candidate("Expensive Phone", 12500m);

        var comparison = ComparisonEngine.Compare([cheap, expensive], SmartphoneAttributes);

        var cheapRow = comparison.Rows.Single(r => r.Candidate.Name == "Cheap Phone");
        var expensiveRow = comparison.Rows.Single(r => r.Candidate.Name == "Expensive Phone");
        Assert.Equal("cheapest in set", cheapRow.DeltasVsBest["price"]);
        Assert.Equal("+2500 UAH vs cheapest", expensiveRow.DeltasVsBest["price"]);
    }

    [Fact]
    public void Highest_spec_value_is_marked_best_and_the_other_gets_a_negative_delta()
    {
        var betterCamera = Candidate("Better Camera", 10000m, specs: [("camera_mp", "50", "MP")]);
        var worseCamera = Candidate("Worse Camera", 10000m, specs: [("camera_mp", "12", "MP")]);

        var comparison = ComparisonEngine.Compare([betterCamera, worseCamera], SmartphoneAttributes);

        var betterRow = comparison.Rows.Single(r => r.Candidate.Name == "Better Camera");
        var worseRow = comparison.Rows.Single(r => r.Candidate.Name == "Worse Camera");
        Assert.Equal("best in set", betterRow.DeltasVsBest["camera_mp"]);
        Assert.Equal("-38MP vs best", worseRow.DeltasVsBest["camera_mp"]);
    }

    [Fact]
    public void Unverified_price_yields_a_null_value_and_a_not_verified_delta_never_a_guess()
    {
        var verified = Candidate("Verified Phone", 10000m);
        var unverified = Candidate("Unverified Phone", null, priceVerified: false);

        var comparison = ComparisonEngine.Compare([verified, unverified], SmartphoneAttributes);

        var row = comparison.Rows.Single(r => r.Candidate.Name == "Unverified Phone");
        Assert.Null(row.ValuesByCriterion["price"]);
        Assert.Equal("not verified", row.DeltasVsBest["price"]);
    }

    [Fact]
    public void Missing_specification_yields_a_null_value_and_a_not_verified_delta()
    {
        var withSpec = Candidate("Has Spec", 10000m, specs: [("camera_mp", "50", "MP")]);
        var withoutSpec = Candidate("Missing Spec", 10000m);

        var comparison = ComparisonEngine.Compare([withSpec, withoutSpec], SmartphoneAttributes);

        var row = comparison.Rows.Single(r => r.Candidate.Name == "Missing Spec");
        Assert.Null(row.ValuesByCriterion["camera_mp"]);
        Assert.Equal("not verified", row.DeltasVsBest["camera_mp"]);
    }

    [Fact]
    public void Rating_is_deterministic_across_repeated_calls_with_the_same_candidates()
    {
        var a = Candidate("Phone A", 10000m, specs: [("camera_mp", "50", "MP"), ("battery_mah", "4000", "mAh")]);
        var b = Candidate("Phone B", 12000m, specs: [("camera_mp", "48", "MP"), ("battery_mah", "3500", "mAh")]);

        var first = ComparisonEngine.Compare([a, b], SmartphoneAttributes);
        var second = ComparisonEngine.Compare([a, b], SmartphoneAttributes);

        Assert.Equal(first.Rows.Select(r => r.Rating), second.Rows.Select(r => r.Rating));
    }

    [Fact]
    public void Candidate_that_is_best_on_every_criterion_rates_higher_than_one_that_is_worse_on_every_criterion()
    {
        var best = Candidate("Best", 10000m, specs: [("camera_mp", "50", "MP"), ("battery_mah", "4000", "mAh")]);
        var worst = Candidate("Worst", 15000m, specs: [("camera_mp", "12", "MP"), ("battery_mah", "2000", "mAh")]);

        var comparison = ComparisonEngine.Compare([best, worst], SmartphoneAttributes);

        var bestRating = comparison.Rows.Single(r => r.Candidate.Name == "Best").Rating;
        var worstRating = comparison.Rows.Single(r => r.Candidate.Name == "Worst").Rating;
        Assert.True(bestRating > worstRating);
    }
}

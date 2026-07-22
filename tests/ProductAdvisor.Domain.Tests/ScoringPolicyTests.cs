using ProductAdvisor.Domain;

namespace ProductAdvisor.Domain.Tests;

public class ScoringPolicyTests
{
    private static ProductCandidate Candidate(
        string name, decimal price, string currency = "UAH", bool priceVerified = true, params (string Key, string Value, string? Unit)[] specs) =>
        new()
        {
            ProductId = Guid.NewGuid(),
            Name = name,
            Price = new Money(price, currency),
            PriceVerified = priceVerified,
            Availability = StockStatus.InStock,
            AvailabilityVerified = true,
            Specifications = specs.Select(s => new Specification(s.Key, s.Value, s.Unit)).ToList(),
        };

    [Fact]
    public void Score_throws_when_budget_is_missing()
    {
        var requirement = new UserRequirement { Category = "smartphones" };

        Assert.Throws<InvalidOperationException>(() => ScoringPolicy.Score(requirement, []));
    }

    [Fact]
    public void Score_hard_excludes_over_budget_candidates()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var cheap = Candidate("Cheap Phone", 14000m);
        var expensive = Candidate("Expensive Phone", 20000m);

        var result = ScoringPolicy.Score(requirement, [cheap, expensive]);

        Assert.Single(result.Items);
        Assert.Equal("Cheap Phone", result.Items[0].Candidate.Name);
    }

    [Fact]
    public void Score_returns_unmet_constraint_explanation_when_nothing_fits_budget()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(500m, "UAH") };
        var onlyOption = Candidate("Flagship Phone", 20000m);

        var result = ScoringPolicy.Score(requirement, [onlyOption]);

        Assert.Empty(result.Items);
        Assert.NotNull(result.UnmetConstraintExplanation);
        Assert.Contains("500", result.UnmetConstraintExplanation);
    }

    [Fact]
    public void Score_never_mixes_a_non_empty_items_list_with_an_unmet_constraint_explanation()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var withinBudget = Candidate("Phone A", 14000m);

        var result = ScoringPolicy.Score(requirement, [withinBudget]);

        Assert.NotEmpty(result.Items);
        Assert.Null(result.UnmetConstraintExplanation);
    }

    [Fact]
    public void Score_excludes_candidates_with_unverified_price()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var unverified = Candidate("Mystery Phone", 14000m, priceVerified: false);

        var result = ScoringPolicy.Score(requirement, [unverified]);

        Assert.Empty(result.Items);
    }

    [Fact]
    public void A_shorter_free_form_requirement_matches_a_longer_more_specific_spec_key()
    {
        // Regression: an LLM extracting "camera" from "a good camera" must still match the
        // catalog's "camera_mp" spec key, not just the exact reverse case.
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["camera"],
        };
        var withCamera = Candidate("Camera Phone", 14000m, specs: ("camera_mp", "50", "MP"));

        var result = ScoringPolicy.Score(requirement, [withCamera]);

        Assert.Contains("camera", result.Items[0].MatchedRequirements);
        Assert.DoesNotContain(result.Items[0].TradeOffs, t => t.Contains("Does not clearly satisfy"));
    }

    [Fact]
    public void A_multi_word_free_form_requirement_still_matches_via_token_overlap()
    {
        // Regression: the LLM phrases the same requirement differently across calls
        // ("camera", "good camera", "camera quality") — all must match "camera_mp" via a
        // shared token, not just an exact substring relationship.
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["good camera"],
        };
        var withCamera = Candidate("Camera Phone", 14000m, specs: ("camera_mp", "50", "MP"));

        var result = ScoringPolicy.Score(requirement, [withCamera]);

        Assert.Contains("good camera", result.Items[0].MatchedRequirements);
        Assert.DoesNotContain(result.Items[0].TradeOffs, t => t.Contains("Does not clearly satisfy"));
    }

    [Fact]
    public void Score_ranks_candidates_matching_more_required_features_higher()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["camera_mp"],
        };
        var withCamera = Candidate("Camera Phone", 14000m, specs: ("camera_mp", "50", "MP"));
        var withoutCamera = Candidate("Basic Phone", 14000m, specs: ("battery_mah", "3000", "mAh"));

        var result = ScoringPolicy.Score(requirement, [withoutCamera, withCamera]);

        Assert.Equal("Camera Phone", result.Items[0].Candidate.Name);
        Assert.Contains("camera_mp", result.Items[0].MatchedRequirements);
    }

    [Fact]
    public void Every_recommended_item_has_at_least_one_trade_off()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var onlyOption = Candidate("Only Option", 14000m, specs: ("camera_mp", "50", "MP"));

        var result = ScoringPolicy.Score(requirement, [onlyOption]);

        Assert.NotEmpty(result.Items[0].TradeOffs);
    }

    [Fact]
    public void A_lower_spec_value_than_the_best_in_set_is_flagged_as_a_trade_off()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var lowBattery = Candidate("Low Battery Phone", 14000m, specs: ("battery_mah", "3000", "mAh"));
        var highBattery = Candidate("High Battery Phone", 14500m, specs: ("battery_mah", "5000", "mAh"));

        var result = ScoringPolicy.Score(requirement, [lowBattery, highBattery]);

        var lowBatteryItem = result.Items.Single(i => i.Candidate.Name == "Low Battery Phone");
        Assert.Contains(lowBatteryItem.TradeOffs, t => t.Contains("battery_mah"));
    }

    [Fact]
    public void Scoring_is_deterministic_across_repeated_calls()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["camera_mp"],
        };
        var candidates = new List<ProductCandidate>
        {
            Candidate("Phone A", 14000m, specs: ("camera_mp", "50", "MP")),
            Candidate("Phone B", 14500m, specs: ("camera_mp", "48", "MP")),
        };

        var first = ScoringPolicy.Score(requirement, candidates);
        var second = ScoringPolicy.Score(requirement, candidates);

        Assert.Equal(first.Items.Select(i => (i.Candidate.Name, i.Score)), second.Items.Select(i => (i.Candidate.Name, i.Score)));
    }
}

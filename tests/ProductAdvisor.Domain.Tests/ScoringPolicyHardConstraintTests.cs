using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Domain.Tests;

/// <summary>
/// Phase 10 (spec.md FR-080–FR-085): a confirmed hard-constraint violation (budget ceiling,
/// currency mismatch, missing required feature, or an explicitly stated availability
/// requirement) excludes a candidate from <see cref="Recommendation.Items"/> entirely — it is
/// never merely ranked lower. A soft preference never excludes anything, only ranks. Nearest
/// alternatives are surfaced only when nothing qualifies (spec.md Assumptions), and always
/// labeled with which constraint(s) they violate.
/// </summary>
public class ScoringPolicyHardConstraintTests
{
    private static ProductCandidate Candidate(
        string name,
        decimal price,
        string currency = "UAH",
        StockStatus availability = StockStatus.InStock,
        bool availabilityVerified = true,
        params (string Key, string Value, string? Unit)[] specs) =>
        new()
        {
            ProductId = Guid.NewGuid(),
            Name = name,
            Price = new Money(price, currency),
            PriceVerified = true,
            Availability = availability,
            AvailabilityVerified = availabilityVerified,
            Specifications = specs.Select(s => new Specification(s.Key, s.Value, s.Unit)).ToList(),
        };

    [Fact]
    public void An_over_budget_candidate_is_excluded_from_items_even_when_it_would_otherwise_rank_well()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var overBudget = Candidate("Expensive Phone", 16000m);
        var withinBudget = Candidate("Cheap Phone", 14000m);

        var result = ScoringPolicy.Score(requirement, [overBudget, withinBudget]);

        Assert.DoesNotContain(result.Items, i => i.Candidate.Name == "Expensive Phone");
        Assert.Contains(result.Items, i => i.Candidate.Name == "Cheap Phone");
    }

    [Fact]
    public void A_currency_mismatched_candidate_is_excluded_from_items()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var wrongCurrency = Candidate("Imported Phone", 100m, currency: "USD");
        var matching = Candidate("Local Phone", 14000m);

        var result = ScoringPolicy.Score(requirement, [wrongCurrency, matching]);

        Assert.DoesNotContain(result.Items, i => i.Candidate.Name == "Imported Phone");
        Assert.Contains(result.Items, i => i.Candidate.Name == "Local Phone");
    }

    [Fact]
    public void A_candidate_missing_a_required_feature_is_excluded_from_items()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["camera_mp"],
        };
        var withoutFeature = Candidate("Basic Phone", 14000m, specs: ("battery_mah", "3000", "mAh"));
        var withFeature = Candidate("Camera Phone", 14000m, specs: ("camera_mp", "50", "MP"));

        var result = ScoringPolicy.Score(requirement, [withoutFeature, withFeature]);

        Assert.DoesNotContain(result.Items, i => i.Candidate.Name == "Basic Phone");
        Assert.Contains(result.Items, i => i.Candidate.Name == "Camera Phone");
    }

    [Fact]
    public void An_out_of_stock_candidate_is_excluded_only_when_the_user_explicitly_required_availability()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            AvailabilityRequirements = ["ships this week"],
        };
        var outOfStock = Candidate("Sold Out Phone", 14000m, availability: StockStatus.OutOfStock);
        var inStock = Candidate("In Stock Phone", 14000m);

        var result = ScoringPolicy.Score(requirement, [outOfStock, inStock]);

        Assert.DoesNotContain(result.Items, i => i.Candidate.Name == "Sold Out Phone");
        Assert.Contains(result.Items, i => i.Candidate.Name == "In Stock Phone");
    }

    [Fact]
    public void An_out_of_stock_candidate_is_not_excluded_when_the_user_stated_no_availability_requirement()
    {
        // FR-085: absent an explicit requirement, availability stays informational only.
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var outOfStock = Candidate("Sold Out Phone", 14000m, availability: StockStatus.OutOfStock);

        var result = ScoringPolicy.Score(requirement, [outOfStock]);

        Assert.Contains(result.Items, i => i.Candidate.Name == "Sold Out Phone");
    }

    [Fact]
    public void A_soft_preference_mismatch_never_excludes_a_candidate_from_items()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            Preferences = ["5G"],
        };
        var noMatchingPreference = Candidate("Plain Phone", 14000m, specs: ("battery_mah", "3000", "mAh"));

        var result = ScoringPolicy.Score(requirement, [noMatchingPreference]);

        Assert.Single(result.Items);
        Assert.Equal("Plain Phone", result.Items[0].Candidate.Name);
    }

    [Fact]
    public void Nearest_alternatives_never_appears_alongside_a_non_empty_items_list()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var overBudget = Candidate("Expensive Phone", 16000m);
        var withinBudget = Candidate("Cheap Phone", 14000m);

        var result = ScoringPolicy.Score(requirement, [overBudget, withinBudget]);

        Assert.NotEmpty(result.Items);
        Assert.Empty(result.NearestAlternatives);
    }

    [Fact]
    public void Nearest_alternatives_are_surfaced_with_their_violated_constraints_when_nothing_qualifies()
    {
        var requirement = new UserRequirement { Category = "smartphones", Budget = new Money(15000m, "UAH") };
        var overBudget = Candidate("Expensive Phone", 16000m);

        var result = ScoringPolicy.Score(requirement, [overBudget]);

        Assert.Empty(result.Items);
        Assert.NotNull(result.UnmetConstraintExplanation);
        var alternative = Assert.Single(result.NearestAlternatives);
        Assert.Equal("Expensive Phone", alternative.Candidate.Name);
        Assert.Contains(alternative.ViolatedConstraints, c => c.StartsWith("budget", StringComparison.Ordinal));
    }

    [Fact]
    public void A_candidate_violating_multiple_hard_constraints_names_every_one_of_them()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["camera_mp"],
        };
        var doubleViolator = Candidate("Bad Fit Phone", 16000m, specs: ("battery_mah", "3000", "mAh"));

        var result = ScoringPolicy.Score(requirement, [doubleViolator]);

        var alternative = Assert.Single(result.NearestAlternatives);
        Assert.Contains(alternative.ViolatedConstraints, c => c.StartsWith("budget", StringComparison.Ordinal));
        Assert.Contains(alternative.ViolatedConstraints, c => c.StartsWith("requiredFeature", StringComparison.Ordinal));
    }
}

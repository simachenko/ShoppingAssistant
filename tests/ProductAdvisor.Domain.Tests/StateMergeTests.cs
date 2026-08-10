using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Domain.Tests;

/// <summary>
/// Deterministic, field-level state merge (spec.md FR-057/FR-058): a field present in a patch
/// replaces the corresponding field; a field absent (null) leaves the existing value untouched —
/// absence is never treated as an instruction to clear. A budget/category change replaces only
/// that field, and a partially-known requirement persists across merges exactly as it stood.
/// </summary>
public class StateMergeTests
{
    [Fact]
    public void A_partial_patch_persists_every_previously_known_field()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["good camera"],
        };

        var merged = requirement.Merge(new RequirementPatch { Preferences = ["lightweight"] });

        Assert.Equal("smartphones", merged.Category);
        Assert.Equal(new Money(15000m, "UAH"), merged.Budget);
        Assert.Equal(["good camera"], merged.RequiredFeatures);
        Assert.Equal(["lightweight"], merged.Preferences);
    }

    [Fact]
    public void An_absent_field_never_clears_the_existing_value()
    {
        var requirement = new UserRequirement { Category = "laptops" };

        var merged = requirement.Merge(new RequirementPatch { Budget = new Money(30000m, "UAH") });

        Assert.Equal("laptops", merged.Category); // untouched, not cleared
        Assert.Equal(new Money(30000m, "UAH"), merged.Budget);
    }

    [Fact]
    public void A_budget_change_replaces_only_the_budget_field()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["good camera"],
            Preferences = ["lightweight"],
        };

        var merged = requirement.Merge(new RequirementPatch { Budget = new Money(20000m, "UAH") });

        Assert.Equal(new Money(20000m, "UAH"), merged.Budget);
        Assert.Equal("smartphones", merged.Category);
        Assert.Equal(["good camera"], merged.RequiredFeatures);
        Assert.Equal(["lightweight"], merged.Preferences);
    }

    [Fact]
    public void A_category_change_replaces_only_the_category_field()
    {
        var requirement = new UserRequirement
        {
            Category = "smartphones",
            Budget = new Money(15000m, "UAH"),
            RequiredFeatures = ["good camera"],
        };

        var merged = requirement.Merge(new RequirementPatch { Category = "laptops" });

        Assert.Equal("laptops", merged.Category);
        Assert.Equal(new Money(15000m, "UAH"), merged.Budget);
        Assert.Equal(["good camera"], merged.RequiredFeatures);
    }

    [Fact]
    public void An_explicit_empty_list_clears_a_list_typed_field()
    {
        var requirement = new UserRequirement { RequiredFeatures = ["good camera"] };

        var merged = requirement.Merge(new RequirementPatch { RequiredFeatures = [] });

        Assert.Empty(merged.RequiredFeatures);
    }

    [Fact]
    public void Language_and_currency_persist_when_a_later_patch_omits_them()
    {
        var requirement = new UserRequirement { Language = "uk", Currency = "UAH" };

        var merged = requirement.Merge(new RequirementPatch { Category = "smartphones" });

        Assert.Equal("uk", merged.Language);
        Assert.Equal("UAH", merged.Currency);
    }

    [Fact]
    public void Sequential_merges_accumulate_every_field_supplied_across_turns()
    {
        var requirement = UserRequirement.Empty;

        requirement = requirement.Merge(new RequirementPatch { Category = "smartphones" });
        requirement = requirement.Merge(new RequirementPatch { Budget = new Money(15000m, "UAH") });
        requirement = requirement.Merge(new RequirementPatch { RequiredFeatures = ["good camera"] });

        Assert.Equal("smartphones", requirement.Category);
        Assert.Equal(new Money(15000m, "UAH"), requirement.Budget);
        Assert.Equal(["good camera"], requirement.RequiredFeatures);
    }
}

using ProductAdvisor.Application.Pipeline;
using ProductAdvisor.Domain;
using Xunit;

namespace ProductAdvisor.Application.Tests;

/// <summary>
/// Phase 12 (spec.md FR-106): a requirementPatch or merge that would exceed the configured max
/// count or max per-entry length is rejected outright — checked against what the merge would
/// actually produce, so the limit applies cumulatively across turns.
/// </summary>
public class RequirementPatchGuardrailsTests
{
    private static readonly RequestGuardrailOptions Options = new() { MaxListEntries = 3, MaxListEntryLength = 10 };

    [Fact]
    public void A_patch_within_the_limits_does_not_throw()
    {
        var patch = new RequirementPatch { RequiredFeatures = ["5G", "camera"] };

        RequirementPatchGuardrails.EnsureWithinLimits(UserRequirement.Empty, patch, Options);
    }

    [Fact]
    public void A_patch_exceeding_the_max_entry_count_is_rejected()
    {
        var patch = new RequirementPatch { RequiredFeatures = ["a", "b", "c", "d"] };

        var ex = Assert.Throws<GuardrailRejectionException>(
            () => RequirementPatchGuardrails.EnsureWithinLimits(UserRequirement.Empty, patch, Options));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void A_patch_with_an_entry_exceeding_the_max_length_is_rejected()
    {
        var patch = new RequirementPatch { Preferences = ["this preference is far too long"] };

        var ex = Assert.Throws<GuardrailRejectionException>(
            () => RequirementPatchGuardrails.EnsureWithinLimits(UserRequirement.Empty, patch, Options));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void The_limit_is_checked_against_the_projected_merge_not_just_the_patch_in_isolation()
    {
        // The current requirement already has entries; a patch that omits the field (leaving the
        // existing value untouched, per the standard merge rule) is still checked against that
        // existing value — the limit is cumulative, not reset to zero just because this turn's
        // patch didn't touch the field.
        var current = UserRequirement.Empty with { AvailabilityRequirements = ["a", "b", "c", "d"] };
        var patch = new RequirementPatch { Category = "smartphones" }; // AvailabilityRequirements omitted.

        var ex = Assert.Throws<GuardrailRejectionException>(
            () => RequirementPatchGuardrails.EnsureWithinLimits(current, patch, Options));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void A_patch_that_replaces_an_over_limit_existing_list_with_a_within_limit_one_does_not_throw()
    {
        var current = UserRequirement.Empty with { RequiredFeatures = ["a", "b", "c", "d"] };
        var patch = new RequirementPatch { RequiredFeatures = ["5G"] };

        RequirementPatchGuardrails.EnsureWithinLimits(current, patch, Options);
    }
}

using ProductAdvisor.Domain;

namespace ProductAdvisor.Application.Pipeline;

/// <summary>
/// FR-106: a configured maximum count and maximum per-entry length for
/// <c>RequiredFeatures</c>/<c>Preferences</c>/<c>AvailabilityRequirements</c> — a
/// <c>requirementPatch</c> or merge that would exceed either is rejected outright rather than
/// silently truncated or accepted. Checked against what the merge would actually produce (the
/// patch's value when given, else the field's existing value) so the limit applies cumulatively
/// across turns, not just to one patch considered in isolation.
/// </summary>
public static class RequirementPatchGuardrails
{
    public static void EnsureWithinLimits(UserRequirement current, RequirementPatch patch, RequestGuardrailOptions options)
    {
        EnsureWithinLimits(patch.RequiredFeatures ?? current.RequiredFeatures, "requiredFeatures", options);
        EnsureWithinLimits(patch.Preferences ?? current.Preferences, "preferences", options);
        EnsureWithinLimits(patch.AvailabilityRequirements ?? current.AvailabilityRequirements, "availabilityRequirements", options);
    }

    private static void EnsureWithinLimits(IReadOnlyList<string> entries, string fieldName, RequestGuardrailOptions options)
    {
        if (entries.Count > options.MaxListEntries)
        {
            throw new GuardrailRejectionException(
                400, $"{fieldName} exceeds the maximum of {options.MaxListEntries} entries.");
        }

        if (entries.Any(entry => entry.Length > options.MaxListEntryLength))
        {
            throw new GuardrailRejectionException(
                400, $"{fieldName} contains an entry exceeding the maximum length of {options.MaxListEntryLength} characters.");
        }
    }
}

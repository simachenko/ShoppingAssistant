namespace ProductAdvisor.Domain;

/// <summary>
/// The latest known snapshot of what the shopper wants, held by a ConversationSession — the
/// sole authoritative source of these fields for every stage after deterministic state merge
/// (spec.md FR-055).
/// </summary>
public sealed record UserRequirement
{
    public string? Category { get; init; }
    public Money? Budget { get; init; }
    public IReadOnlyList<string> RequiredFeatures { get; init; } = [];
    public IReadOnlyList<string> Preferences { get; init; } = [];
    public IReadOnlyList<string> AvailabilityRequirements { get; init; } = [];
    public string Language { get; init; } = "en";
    public string Currency { get; init; } = "USD";
    public string? Units { get; init; }

    /// <summary>
    /// The essential-information bar (FR-002/SC-005): a recommendation may only be produced
    /// once both Category and Budget are known — otherwise a ClarificationQuestion is required.
    /// </summary>
    public bool HasEssentialInformation => !string.IsNullOrWhiteSpace(Category) && Budget is not null;

    public static UserRequirement Empty { get; } = new();

    /// <summary>
    /// Deterministic, field-level merge of a schema-valid <see cref="RequirementPatch"/> (spec.md
    /// FR-056–FR-059): a field the patch supplies replaces the corresponding field here; a field
    /// the patch omits (null) leaves the existing value here untouched — absence is never
    /// treated as an instruction to clear. Never called with anything other than a validated
    /// patch, and never performed by the language model directly writing this object.
    /// </summary>
    public UserRequirement Merge(RequirementPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        return this with
        {
            Category = patch.Category ?? Category,
            Budget = patch.Budget ?? Budget,
            RequiredFeatures = patch.RequiredFeatures ?? RequiredFeatures,
            Preferences = patch.Preferences ?? Preferences,
            AvailabilityRequirements = patch.AvailabilityRequirements ?? AvailabilityRequirements,
            Language = patch.Language ?? Language,
            Currency = patch.Currency ?? Currency,
            Units = patch.Units ?? Units,
        };
    }
}

namespace ProductAdvisor.Domain;

/// <summary>The latest known snapshot of what the shopper wants, held by a ConversationSession.</summary>
public sealed record UserRequirement
{
    public string? Category { get; init; }
    public Money? Budget { get; init; }
    public IReadOnlyList<string> RequiredFeatures { get; init; } = [];
    public IReadOnlyList<string> Preferences { get; init; } = [];
    public string Language { get; init; } = "en";
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// The essential-information bar (FR-002/SC-005): a recommendation may only be produced
    /// once both Category and Budget are known — otherwise a ClarificationQuestion is required.
    /// </summary>
    public bool HasEssentialInformation => !string.IsNullOrWhiteSpace(Category) && Budget is not null;

    public static UserRequirement Empty { get; } = new();
}

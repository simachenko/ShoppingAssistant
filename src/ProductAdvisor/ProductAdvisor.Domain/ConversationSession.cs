namespace ProductAdvisor.Domain;

/// <summary>Aggregate root for the Product Advisor bounded context — the "semantic UI" state.</summary>
public sealed class ConversationSession
{
    private readonly List<ConversationMessage> _messages = [];

    // A concrete List backing field (not a `{ get; } = []` interface-typed auto-property) —
    // collection expressions targeted at a read-only interface type like IReadOnlyList<T> may
    // compile to a fixed-size array, which throws when EF Core's owned-JSON collection
    // materialization tries to .Add() into it while hydrating from the database.
    private List<SearchResultReference> _lastSearchResults = [];

    public Guid SessionId { get; private set; }

    /// <summary>
    /// The owning user's stable identifier (Google token's <c>sub</c> claim), set once at
    /// creation and never changed — every session-scoped request MUST be checked against this
    /// before returning the session's content (FR-031, research.md §17).
    /// </summary>
    public string UserId { get; private set; } = "";

    public ConversationState State { get; private set; } = ConversationState.Collecting;
    public IReadOnlyList<ConversationMessage> Messages => _messages;
    public UserRequirement CurrentRequirement { get; private set; } = UserRequirement.Empty;
    public ClarificationQuestion? PendingClarification { get; private set; }

    /// <summary>
    /// The most recently shown search/recommendation/comparison candidates (FR-022) — replaced,
    /// never appended to, each time a new result set is produced, so an ordinal follow-up
    /// ("the first two") always resolves against exactly one, current set (research.md §15).
    /// </summary>
    public IReadOnlyList<SearchResultReference> LastSearchResults => _lastSearchResults;

    public ConversationSession(Guid sessionId, string userId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));

        SessionId = sessionId;
        UserId = userId;
    }

    private ConversationSession()
    {
        // EF Core materialization only.
    }

    public void AddMessage(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
    }

    /// <summary>
    /// Replaces the known requirement (e.g., the user changed their budget mid-conversation —
    /// FR-011). Always drops back to Collecting: a changed constraint supersedes any prior
    /// recommendation/comparison rather than silently merging with it.
    /// </summary>
    public void UpdateRequirement(UserRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        CurrentRequirement = requirement;
        PendingClarification = null;
        State = ConversationState.Collecting;
    }

    /// <summary>
    /// Deterministic, field-level merge of a schema-valid <see cref="RequirementPatch"/> into
    /// <see cref="CurrentRequirement"/> (spec.md FR-056–FR-059) — the state-merge stage's own
    /// operation, distinct from <see cref="UpdateRequirement"/>'s wholesale replace. Also drops
    /// back to Collecting, consistent with FR-011: a changed constraint supersedes any prior
    /// recommendation rather than silently merging with it.
    /// </summary>
    public void MergeRequirement(RequirementPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        CurrentRequirement = CurrentRequirement.Merge(patch);
        PendingClarification = null;
        State = ConversationState.Collecting;
    }

    public void AskClarification(ClarificationQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        PendingClarification = question;
        State = ConversationState.Collecting;
    }

    /// <summary>Only reachable once the essential-information bar (FR-002) is satisfied.</summary>
    public void StartRecommending()
    {
        if (!CurrentRequirement.HasEssentialInformation)
        {
            throw new InvalidOperationException(
                "Cannot start recommending before Category and Budget are both known.");
        }

        PendingClarification = null;
        State = ConversationState.Recommending;
    }

    public void StartComparing()
    {
        PendingClarification = null;
        State = ConversationState.Comparing;
    }

    /// <summary>Replaces (never appends to) the session's memory of what was last shown (FR-022).</summary>
    public void SetLastSearchResults(IReadOnlyList<SearchResultReference> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        _lastSearchResults = [.. results];
    }
}

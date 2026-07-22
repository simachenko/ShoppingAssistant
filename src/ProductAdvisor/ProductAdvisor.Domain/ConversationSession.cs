namespace ProductAdvisor.Domain;

/// <summary>Aggregate root for the Product Advisor bounded context — the "semantic UI" state.</summary>
public sealed class ConversationSession
{
    private readonly List<ConversationMessage> _messages = [];

    public Guid SessionId { get; private set; }
    public ConversationState State { get; private set; } = ConversationState.Collecting;
    public IReadOnlyList<ConversationMessage> Messages => _messages;
    public UserRequirement CurrentRequirement { get; private set; } = UserRequirement.Empty;
    public ClarificationQuestion? PendingClarification { get; private set; }

    public ConversationSession(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        SessionId = sessionId;
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
}

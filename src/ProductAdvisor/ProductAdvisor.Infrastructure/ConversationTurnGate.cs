using System.Collections.Concurrent;

namespace ProductAdvisor.Infrastructure;

/// <summary>
/// Tracks which sessions currently have a turn in flight, so a second message for the same
/// session is rejected rather than processed concurrently with the first (FR-024, SC-014). A
/// single process-wide singleton is sufficient at this system's scale — no distributed lock is
/// needed since only one Advisor instance is ever deployed for the demo (plan.md Scale/Scope).
/// </summary>
public sealed class ConversationTurnGate
{
    private readonly ConcurrentDictionary<Guid, byte> _inProgress = new();

    /// <summary>Returns <see langword="true"/> and marks the session busy if no turn for it is
    /// already in flight; returns <see langword="false"/> without changing anything otherwise.</summary>
    public bool TryEnter(Guid sessionId) => _inProgress.TryAdd(sessionId, 0);

    /// <summary>Always call in a <c>finally</c> block after <see cref="TryEnter"/> returned
    /// <see langword="true"/>, regardless of how the turn ended.</summary>
    public void Exit(Guid sessionId) => _inProgress.TryRemove(sessionId, out _);
}

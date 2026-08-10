using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

public interface IConversationSessionRepository
{
    Task<ConversationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    Task AddAsync(ConversationSession session, CancellationToken cancellationToken);

    /// <summary>FR-119: user-initiated deletion of a single session. A no-op (not an error) when
    /// <paramref name="sessionId"/> doesn't exist — deleting is idempotent.</summary>
    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>FR-119: user-initiated deletion of every session belonging to <paramref name="userId"/>.</summary>
    Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

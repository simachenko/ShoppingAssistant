using ProductAdvisor.Domain;

namespace ProductAdvisor.Application;

public interface IConversationSessionRepository
{
    Task<ConversationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    Task AddAsync(ConversationSession session, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

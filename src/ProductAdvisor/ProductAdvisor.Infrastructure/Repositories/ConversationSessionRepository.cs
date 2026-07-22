using Microsoft.EntityFrameworkCore;
using ProductAdvisor.Application;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Repositories;

public sealed class ConversationSessionRepository(AdvisorDbContext dbContext) : IConversationSessionRepository
{
    public Task<ConversationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

    public async Task AddAsync(ConversationSession session, CancellationToken cancellationToken) =>
        await dbContext.Sessions.AddAsync(session, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

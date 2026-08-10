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

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDeleteAsync bypasses the change tracker — nothing further to do either way;
        // deleting an id that doesn't exist is not an error (FR-119 is idempotent by nature).
        _ = deleted;
    }

    public async Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken) =>
        await dbContext.Sessions
            .Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure;

/// <summary>
/// Persists conversation history only — the Advisor never keeps a shadow copy of Catalog or
/// Pricing data; those are fetched per-request via MCP tool calls (research.md §1).
/// </summary>
public sealed class AdvisorDbContext(DbContextOptions<AdvisorDbContext> options) : DbContext(options)
{
    public const string Schema = "advisor";

    public DbSet<ConversationSession> Sessions => Set<ConversationSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdvisorDbContext).Assembly);
    }
}

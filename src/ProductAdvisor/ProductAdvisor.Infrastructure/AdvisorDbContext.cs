using Microsoft.EntityFrameworkCore;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure;

/// <summary>
/// Persists conversation history and the store-policy knowledge base — the Advisor never keeps a
/// shadow copy of Catalog or Pricing data; those are fetched per-request via MCP tool calls
/// (research.md §1). Store documents are not product data: they are this service's own content,
/// never a cache of another bounded context's (002 research.md §1).
/// </summary>
public sealed class AdvisorDbContext(DbContextOptions<AdvisorDbContext> options) : DbContext(options)
{
    public const string Schema = "advisor";

    public DbSet<ConversationSession> Sessions => Set<ConversationSession>();

    /// <summary>
    /// The store-policy knowledge base (002 data-model.md). <see cref="DocumentChunk"/> has no
    /// <see cref="DbSet{TEntity}"/> of its own — it is reached through
    /// <see cref="StoreDocument.Chunks"/>, or directly by the hybrid-search query's raw SQL.
    /// </summary>
    public DbSet<StoreDocument> StoreDocuments => Set<StoreDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Emitted by the migration as CREATE EXTENSION — pgvector's column type and index methods
        // are unavailable until it exists (002 research.md §6).
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdvisorDbContext).Assembly);
    }
}

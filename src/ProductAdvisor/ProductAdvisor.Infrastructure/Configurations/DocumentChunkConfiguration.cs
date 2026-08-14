using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Configurations;

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    /// <summary>
    /// The generated full-text column backing hybrid search's keyword leg (research.md §8). Not
    /// mapped as an EF property: the hybrid-search query is raw SQL regardless, so mapping it
    /// would push an <c>NpgsqlTsVector</c> dependency into the Domain entity for no benefit. It is
    /// created, together with its GIN index, by the migration's own SQL.
    /// </summary>
    public const string ContentTsVectorColumn = "content_tsvector";

    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks");
        builder.HasKey(c => c.ChunkId);
        builder.Property(c => c.ChunkId).ValueGeneratedNever();

        builder.Property(c => c.DocumentId).IsRequired();
        builder.Property(c => c.Order).IsRequired();
        builder.Property(c => c.Content).IsRequired();

        // Domain holds a plain float[] so it carries no persistence-technology dependency
        // (constitution Principle I); the conversion to pgvector's own type happens here, at the
        // mapping boundary, and nowhere else (data-model.md, research.md §6).
        // The explicit ValueComparer is required, not cosmetic: without it EF compares the float[]
        // by reference, so a re-embedded chunk whose array instance changed but whose contents are
        // identical would be written back needlessly — and, worse, an in-place edit of the same
        // array instance would not be detected as a change at all.
        builder.Property(c => c.Embedding)
            .HasColumnType($"vector({DocumentChunk.EmbeddingDimension})")
            .HasConversion(
                v => new Vector(v),
                v => v.ToArray(),
                new ValueComparer<float[]>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f)),
                    v => v.ToArray()))
            .IsRequired();

        // Denormalized from the parent document so a retrieval never needs a join to apply the
        // mandatory store filter or the language/type ranking preferences (data-model.md).
        builder.Property(c => c.StoreId).IsRequired();
        builder.Property(c => c.Language).IsRequired();
        builder.Property(c => c.DocumentType).HasConversion<string>().IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().IsRequired();

        builder.HasIndex(c => new { c.DocumentId, c.Order }).IsUnique();

        // Mirrors the parent's index: every hybrid-search leg filters chunks on exactly these two
        // before ranking anything (FR-012/FR-020).
        builder.HasIndex(c => new { c.StoreId, c.Status });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.Configurations;

public sealed class StoreDocumentConfiguration : IEntityTypeConfiguration<StoreDocument>
{
    public void Configure(EntityTypeBuilder<StoreDocument> builder)
    {
        builder.ToTable("store_documents");
        builder.HasKey(d => d.DocumentId);
        builder.Property(d => d.DocumentId).ValueGeneratedNever();

        builder.Property(d => d.StoreId).IsRequired();
        builder.Property(d => d.Title).IsRequired();
        builder.Property(d => d.Language).IsRequired();

        // Stored as text, not an int: an enum member added later (FR-015's "extensible set") must
        // never silently re-map already-stored rows, which ordinal storage would risk.
        builder.Property(d => d.DocumentType).HasConversion<string>().IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().IsRequired();

        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.SupersededAt);
        builder.Property(d => d.SupersedesDocumentId);

        // The shape every retrieval filters on (FR-020/FR-012) — store first, since it is the
        // mandatory predicate, then the two that narrow an already-store-scoped set.
        builder.HasIndex(d => new { d.StoreId, d.Status, d.DocumentType });

        // A real child table rather than owned-JSON (the treatment ConversationSession's own
        // collections get): chunks are queried directly, independently of their parent, by the
        // hybrid-search query — JSON storage could neither index an embedding nor rank a fragment.
        builder.HasMany(d => d.Chunks)
            .WithOne()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Chunks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

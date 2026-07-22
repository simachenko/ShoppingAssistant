using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.ProductId);
        builder.Property(p => p.ProductId).ValueGeneratedNever();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.BrandId).IsRequired();
        builder.Property(p => p.CategoryId).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();

        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => p.BrandId);

        // Specification is an owned value-object collection, stored inline as a jsonb column —
        // it belongs entirely to Product, never queried or referenced independently.
        builder.OwnsMany(p => p.Specifications, spec =>
        {
            spec.ToJson();
            spec.Property(s => s.Key).IsRequired();
            spec.Property(s => s.Value).IsRequired();
            spec.Property(s => s.Unit);
        });

        builder.Property(p => p.SearchKeywords)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("search_keywords")
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                v => v.ToList()));
    }
}

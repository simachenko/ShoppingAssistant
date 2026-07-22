using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.CategoryId);
        builder.Property(c => c.CategoryId).ValueGeneratedNever();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);

        builder.Property(c => c.ComparableAttributeKeys)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("comparable_attribute_keys")
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                v => v.ToList()));
    }
}

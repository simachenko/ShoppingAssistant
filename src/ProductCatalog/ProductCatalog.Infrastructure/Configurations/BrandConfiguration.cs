using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductCatalog.Domain;

namespace ProductCatalog.Infrastructure.Configurations;

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");
        builder.HasKey(b => b.BrandId);
        builder.Property(b => b.BrandId).ValueGeneratedNever();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(200);
    }
}

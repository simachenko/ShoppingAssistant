using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PricingAvailability.Domain;

namespace PricingAvailability.Infrastructure.Configurations;

public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");
        builder.HasKey(o => o.OfferId);
        builder.Property(o => o.OfferId).ValueGeneratedNever();

        // ProductId references Catalog's Product by value only — no FK, no cross-service query.
        builder.Property(o => o.ProductId).IsRequired();
        builder.HasIndex(o => o.ProductId).IsUnique();

        builder.Property(o => o.Availability).HasConversion<string>().IsRequired();
        builder.Property(o => o.AsOf).IsRequired();
        builder.Property(o => o.Source).IsRequired().HasMaxLength(200);

        builder.OwnsOne(o => o.Price, price =>
        {
            price.Property(p => p.Amount).HasColumnName("price_amount").HasPrecision(18, 2).IsRequired();
            price.Property(p => p.Currency).HasColumnName("price_currency").HasMaxLength(3).IsRequired();
        });

        builder.Navigation(o => o.Price).IsRequired();

        builder.OwnsOne(o => o.Discount, discount =>
        {
            discount.Property(d => d.PercentOff).HasColumnName("discount_percent_off").HasPrecision(5, 2);
            discount.Property(d => d.ValidUntil).HasColumnName("discount_valid_until");
        });
    }
}

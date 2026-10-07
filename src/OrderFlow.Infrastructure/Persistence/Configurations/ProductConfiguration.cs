using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Domain.Catalog;

namespace OrderFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000).IsRequired();
        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
        builder.Property(p => p.StockQuantity).IsRequired();

        // Optimistic concurrency token: PostgreSQL system column xmin.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

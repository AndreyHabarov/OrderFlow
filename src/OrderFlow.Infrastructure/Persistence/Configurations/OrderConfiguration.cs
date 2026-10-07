using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.HasIndex(o => o.CustomerId);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100);

        // One order per (customer, key). PostgreSQL treats NULLs as distinct, so orders without a key are not affected.
        builder.HasIndex(o => new { o.CustomerId, o.IdempotencyKey }).IsUnique();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.CancellationReason).HasMaxLength(500).IsRequired();
        builder.Ignore(o => o.Total);

        builder.HasMany(o => o.Items).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");
        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();
        builder.Ignore(i => i.LineTotal);
        builder.ComplexProperty(i => i.UnitPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("unit_price_amount").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3);
        });
    }
}

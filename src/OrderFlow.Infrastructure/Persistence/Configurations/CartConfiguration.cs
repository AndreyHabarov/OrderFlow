using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Domain.Carts;

namespace OrderFlow.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasIndex(c => c.CustomerId).IsUnique();
        builder.Ignore(c => c.Total);

        builder.HasMany(c => c.Items).WithOne().HasForeignKey("CartId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items");
        builder.Property<int>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");
        builder.Property(i => i.ProductId).IsRequired();

        // A product appears once per cart; concurrent adds that would create a second row are rejected and retried.
        builder.HasIndex("CartId", nameof(CartItem.ProductId)).IsUnique();
        builder.Property(i => i.Quantity).IsRequired();
        builder.Ignore(i => i.LineTotal);
        builder.ComplexProperty(i => i.UnitPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("unit_price_amount").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3);
        });
    }
}

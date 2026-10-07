using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;

namespace OrderFlow.UnitTests.Domain;

public class ProductAndCartTests
{
    [Fact]
    public void Reserve_decreases_stock_and_release_restores_it()
    {
        var product = Product.Create("Mouse", string.Empty, new Money(25m), 5);

        product.Reserve(3);
        Assert.Equal(2, product.StockQuantity);

        product.Release(3);
        Assert.Equal(5, product.StockQuantity);
    }

    [Fact]
    public void Reserve_more_than_available_is_rejected_and_keeps_stock()
    {
        var product = Product.Create("Mouse", string.Empty, new Money(25m), 2);

        Assert.Throws<DomainException>(() => product.Reserve(3));
        Assert.Equal(2, product.StockQuantity);
    }

    [Fact]
    public void Cart_merges_same_product_and_computes_total()
    {
        var cart = Cart.CreateFor(Guid.NewGuid());
        var productId = Guid.NewGuid();

        cart.AddItem(productId, 1, new Money(10m));
        cart.AddItem(productId, 2, new Money(10m));

        Assert.Single(cart.Items);
        Assert.Equal(30m, cart.Total.Amount);
    }

    [Fact]
    public void Cart_rejects_non_positive_quantity_and_unknown_removal()
    {
        var cart = Cart.CreateFor(Guid.NewGuid());

        Assert.Throws<DomainException>(() => cart.AddItem(Guid.NewGuid(), 0, new Money(1m)));
        Assert.Throws<DomainException>(() => cart.RemoveItem(Guid.NewGuid()));
    }

    [Fact]
    public void Money_rejects_negative_amounts_and_mixed_currencies()
    {
        Assert.Throws<DomainException>(() => new Money(-1m));
        Assert.Throws<DomainException>(() => new Money(1m, "USD").Add(new Money(1m, "EUR")));
    }
}

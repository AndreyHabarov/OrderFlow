using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;

namespace OrderFlow.UnitTests.Domain;

public class ProductAndCartTests
{
    [Fact]
    public void Product_requires_a_name_and_keeps_an_explicit_id()
    {
        Assert.Throws<DomainException>(() => Product.Create(" ", "d", new Money(1m)));

        var id = Guid.NewGuid();
        var product = Product.Create(id, "  Mouse ", "d", new Money(25m));

        Assert.Equal(id, product.Id);
        Assert.Equal("Mouse", product.Name);
        Assert.Equal(25m, product.Price.Amount);
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

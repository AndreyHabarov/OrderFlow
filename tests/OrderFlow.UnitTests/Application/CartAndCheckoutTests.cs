using FluentValidation;
using OrderFlow.Application.Carts;
using OrderFlow.Application.Common;
using OrderFlow.Application.Orders;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.UnitTests.Application;

public class CartAndCheckoutTests
{
    [Fact]
    public async Task Adding_the_same_product_twice_merges_quantities()
    {
        var product = TestApp.Product("Mouse", price: 25m);
        var app = new TestApp(null, product);

        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        var cart = await app.Sender.Send(new AddCartItemCommand(product.Id, 2));

        Assert.Single(cart.Items);
        Assert.Equal(3, cart.Items[0].Quantity);
        Assert.Equal(75m, cart.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Invalid_quantity_is_rejected_by_validation(int quantity)
    {
        var product = TestApp.Product("Mouse");
        var app = new TestApp(null, product);

        await Assert.ThrowsAsync<ValidationException>(() => app.Sender.Send(new AddCartItemCommand(product.Id, quantity)));
        Assert.Equal(0, app.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Adding_an_unknown_product_is_not_found()
    {
        var app = new TestApp();

        await Assert.ThrowsAsync<NotFoundException>(() => app.Sender.Send(new AddCartItemCommand(Guid.NewGuid(), 1)));
    }

    [Fact]
    public async Task Checkout_creates_order_reserves_stock_empties_cart_and_invalidates_cache()
    {
        var keyboard = TestApp.Product("Keyboard", price: 50m, stock: 5);
        var mouse = TestApp.Product("Mouse", price: 20m, stock: 3);
        var app = new TestApp(null, keyboard, mouse);
        await app.Sender.Send(new AddCartItemCommand(keyboard.Id, 2));
        await app.Sender.Send(new AddCartItemCommand(mouse.Id, 1));

        var order = await app.Sender.Send(new CheckoutCommand());

        Assert.Equal(nameof(OrderStatus.StockReserved), order.Status);
        Assert.Equal(120m, order.Total);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(3, keyboard.StockQuantity);
        Assert.Equal(2, mouse.StockQuantity);
        Assert.Empty(app.Carts.Store.Single().Items);
        Assert.Single(app.Orders.Store);
        Assert.Equal(1, app.Cache.Invalidations);
    }

    [Fact]
    public async Task Checkout_with_insufficient_stock_fails_and_creates_nothing()
    {
        var product = TestApp.Product("Keyboard", stock: 1);
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 2));
        var savesBefore = app.UnitOfWork.Saves;

        await Assert.ThrowsAsync<DomainException>(() => app.Sender.Send(new CheckoutCommand()));

        Assert.Empty(app.Orders.Store);
        Assert.Equal(savesBefore, app.UnitOfWork.Saves);
        Assert.Equal(0, app.Cache.Invalidations);
    }

    [Fact]
    public async Task Checkout_with_empty_cart_is_rejected()
    {
        var app = new TestApp();

        await Assert.ThrowsAsync<DomainException>(() => app.Sender.Send(new CheckoutCommand()));
    }

    [Fact]
    public async Task Order_snapshot_keeps_the_price_at_checkout_time()
    {
        var product = TestApp.Product("Keyboard", price: 50m);
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));

        var order = await app.Sender.Send(new CheckoutCommand());
        var fetched = await app.Sender.Send(new GetOrderQuery(order.Id));

        Assert.Equal(50m, fetched.Items[0].UnitPrice);
        Assert.Equal("Keyboard", fetched.Items[0].ProductName);
    }

    [Fact]
    public async Task Other_customers_cannot_see_the_order()
    {
        var product = TestApp.Product("Keyboard");
        var owner = new TestApp(null, product);
        await owner.Sender.Send(new AddCartItemCommand(product.Id, 1));
        var order = await owner.Sender.Send(new CheckoutCommand());

        // Same stores, different identity.
        var stranger = new TestApp(Guid.NewGuid(), product);
        foreach (var o in owner.Orders.Store)
        {
            stranger.Orders.Store.Add(o);
        }

        await Assert.ThrowsAsync<NotFoundException>(() => stranger.Sender.Send(new GetOrderQuery(order.Id)));
    }
}

using FluentValidation;
using OrderFlow.Application.Carts;
using OrderFlow.Application.Common;
using OrderFlow.Application.Orders;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.UnitTests.Application;

public class CartAndCheckoutTests
{
    private static async Task<CheckoutResult> Checkout(TestApp app, string key = "key-1") =>
        await app.Sender.Send(new CheckoutCommand(key));

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
    public async Task Checkout_creates_a_pending_order_empties_the_cart_and_announces_the_order()
    {
        var keyboard = TestApp.Product("Keyboard", price: 50m);
        var mouse = TestApp.Product("Mouse", price: 20m);
        var app = new TestApp(null, keyboard, mouse);
        await app.Sender.Send(new AddCartItemCommand(keyboard.Id, 2));
        await app.Sender.Send(new AddCartItemCommand(mouse.Id, 1));

        var result = await Checkout(app);

        Assert.False(result.Replayed);
        Assert.Equal(nameof(OrderStatus.Pending), result.Order.Status); // Inventory and Payments decide the rest
        Assert.Equal(120m, result.Order.Total);
        Assert.Equal(2, result.Order.Items.Count);
        Assert.Empty(app.Carts.Store.Single().Items);
        Assert.Single(app.Orders.Store);
        Assert.Equal([result.Order.Id], app.Events.Created);
    }

    [Fact]
    public async Task Checkout_still_succeeds_when_the_broker_is_down_and_the_order_stays_pending()
    {
        // Known gap of stage 2 (ADR 0006): the commit happened, the announcement did not. Stage 3's outbox fixes it.
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        app.Events.FailPublishing = true;

        var result = await Checkout(app);

        Assert.Equal(nameof(OrderStatus.Pending), result.Order.Status);
        Assert.Single(app.Orders.Store);
        Assert.Empty(app.Events.Created);
    }

    [Fact]
    public async Task Checkout_with_empty_cart_is_rejected()
    {
        var app = new TestApp();

        await Assert.ThrowsAsync<DomainException>(() => Checkout(app));
    }

    [Fact]
    public async Task Checkout_rejects_a_cart_with_a_product_that_left_the_catalog()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        var cart = app.Carts.Store.Single();
        cart.AddItem(Guid.NewGuid(), 1, new Money(5m)); // a product the catalog does not know

        var error = await Assert.ThrowsAsync<DomainException>(() => Checkout(app));

        Assert.Contains("no longer available", error.Message, StringComparison.Ordinal);
        Assert.Empty(app.Orders.Store);
        Assert.Empty(app.Events.Created);
    }

    [Fact]
    public async Task Order_snapshot_keeps_the_price_at_checkout_time()
    {
        var product = TestApp.Product("Keyboard", price: 50m);
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));

        var created = await Checkout(app);
        var fetched = await app.Sender.Send(new GetOrderQuery(created.Order.Id));

        Assert.Equal(50m, fetched.Items[0].UnitPrice);
        Assert.Equal("Keyboard", fetched.Items[0].ProductName);
    }

    [Fact]
    public async Task Other_customers_cannot_see_the_order()
    {
        var product = TestApp.Product("Keyboard");
        var owner = new TestApp(null, product);
        await owner.Sender.Send(new AddCartItemCommand(product.Id, 1));
        var created = await Checkout(owner);

        // Same stores, different identity.
        var stranger = new TestApp(Guid.NewGuid(), product);
        foreach (var o in owner.Orders.Store)
        {
            stranger.Orders.Store.Add(o);
        }

        await Assert.ThrowsAsync<NotFoundException>(() => stranger.Sender.Send(new GetOrderQuery(created.Order.Id)));
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        var app = new TestApp();

        await Assert.ThrowsAsync<ValidationException>(() => Checkout(app, key: ""));
    }

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_same_order_and_announces_it_once()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 2));

        var first = await Checkout(app, "same-key");
        var second = await Checkout(app, "same-key");

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Order.Id, second.Order.Id);
        Assert.Single(app.Orders.Store);
        Assert.Single(app.Events.Created);
    }

    [Fact]
    public async Task A_different_key_after_the_cart_was_emptied_is_a_new_attempt_and_fails()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        await Checkout(app, "key-a");

        await Assert.ThrowsAsync<DomainException>(() => Checkout(app, "key-b"));
    }

    [Fact]
    public async Task Concurrent_duplicate_is_resolved_by_returning_the_winning_order()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));

        // While we are saving, another request with the same key commits first and our insert hits the unique index.
        var winner = Order.Create(
            app.CustomerId,
            [new OrderItem(product.Id, "Keyboard", 1, new Money(10m))],
            DateTimeOffset.UtcNow,
            "race-key");
        app.UnitOfWork.OnNextSave = () =>
        {
            app.Orders.Store.RemoveAll(o => o.IdempotencyKey == "race-key");
            app.Orders.Store.Add(winner);
            throw new UniqueViolationSimulated();
        };

        var result = await Checkout(app, "race-key");

        Assert.True(result.Replayed);
        Assert.Equal(winner.Id, result.Order.Id);
        Assert.Empty(app.Events.Created); // the winner announces its own order
    }

    [Fact]
    public async Task Duplicate_that_finds_the_cart_already_emptied_by_the_winner_gets_the_winning_order()
    {
        // Race window found by CI: the fast-path lookup ran before the winner committed, the cart read ran after.
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        var first = await Checkout(app, "race-key"); // the winner: order exists, cart is empty
        app.Orders.HideNextLookups = 1; // the duplicate's first lookup still saw nothing

        var duplicate = await Checkout(app, "race-key");

        Assert.True(duplicate.Replayed);
        Assert.Equal(first.Order.Id, duplicate.Order.Id);
        Assert.Single(app.Orders.Store);
    }

    [Fact]
    public async Task Duplicate_that_loses_on_a_version_conflict_gets_the_winning_order()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        var winner = Order.Create(app.CustomerId, [new OrderItem(product.Id, "Keyboard", 1, new Money(10m))], DateTimeOffset.UtcNow, "race-key");
        app.UnitOfWork.OnNextSave = () =>
        {
            app.Orders.Store.RemoveAll(o => o.IdempotencyKey == "race-key"); // our own insert is rolled back
            app.Orders.Store.Add(winner);
            throw new ConcurrencySimulated();
        };

        var result = await Checkout(app, "race-key");

        Assert.True(result.Replayed);
        Assert.Equal(winner.Id, result.Order.Id);
    }

    [Fact]
    public async Task A_version_conflict_that_is_not_a_duplicate_is_not_hidden()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);
        await app.Sender.Send(new AddCartItemCommand(product.Id, 1));
        app.UnitOfWork.OnNextSave = () =>
        {
            app.Orders.Store.RemoveAll(o => o.IdempotencyKey == "my-own-key"); // our own insert is rolled back
            throw new ConcurrencySimulated();
        };

        await Assert.ThrowsAsync<ConcurrencySimulated>(() => Checkout(app, "my-own-key"));
        Assert.Empty(app.Events.Created);
    }
}

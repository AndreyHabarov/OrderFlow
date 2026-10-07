using OrderFlow.Application.Orders;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.UnitTests.Application;

/// <summary>How an order reacts to events from Inventory and Payments, including repeats and late arrivals.</summary>
public class OrderEventTests
{
    private static (TestApp App, Order Order) AppWithOrder()
    {
        var app = new TestApp();
        var order = Order.Create(
            app.CustomerId,
            [new OrderItem(Guid.NewGuid(), "Keyboard", 1, new Money(50m))],
            DateTimeOffset.UtcNow,
            "key");
        app.Orders.Store.Add(order);
        return (app, order);
    }

    [Fact]
    public async Task The_happy_path_moves_a_pending_order_to_confirmed_and_announces_it()
    {
        var (app, order) = AppWithOrder();

        await app.Sender.Send(new MarkOrderStockReservedCommand(order.Id));
        Assert.Equal(OrderStatus.StockReserved, order.Status);

        await app.Sender.Send(new ConfirmOrderPaymentCommand(order.Id));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal([order.Id], app.Events.Confirmed);
    }

    [Fact]
    public async Task A_payment_that_arrives_before_the_stock_event_still_confirms_the_order()
    {
        var (app, order) = AppWithOrder();

        await app.Sender.Send(new ConfirmOrderPaymentCommand(order.Id)); // the order is still Pending

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task Repeated_events_change_nothing_and_never_throw()
    {
        var (app, order) = AppWithOrder();
        await app.Sender.Send(new MarkOrderStockReservedCommand(order.Id));
        await app.Sender.Send(new MarkOrderStockReservedCommand(order.Id)); // duplicate delivery
        await app.Sender.Send(new ConfirmOrderPaymentCommand(order.Id));
        var savesAfterConfirm = app.UnitOfWork.Saves;

        await app.Sender.Send(new MarkOrderStockReservedCommand(order.Id)); // late duplicate after the order moved on
        await app.Sender.Send(new ConfirmOrderPaymentCommand(order.Id)); // duplicate of the payment event

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(savesAfterConfirm, app.UnitOfWork.Saves);
        Assert.Equal(2, app.Events.Confirmed.Count); // the repeat re-announces, in case the first announcement was lost
    }

    [Fact]
    public async Task A_refusal_cancels_a_pending_or_reserved_order_with_the_reason()
    {
        var (app, pending) = AppWithOrder();
        await app.Sender.Send(new CancelOrderCommand(pending.Id, "Not enough stock"));
        Assert.Equal(OrderStatus.Cancelled, pending.Status);
        Assert.Equal("Not enough stock", pending.CancellationReason);

        var (app2, reserved) = AppWithOrder();
        await app2.Sender.Send(new MarkOrderStockReservedCommand(reserved.Id));
        await app2.Sender.Send(new CancelOrderCommand(reserved.Id, "Payment failed: declined"));
        Assert.Equal(OrderStatus.Cancelled, reserved.Status);
    }

    [Fact]
    public async Task A_late_refusal_does_not_cancel_a_confirmed_order()
    {
        var (app, order) = AppWithOrder();
        await app.Sender.Send(new ConfirmOrderPaymentCommand(order.Id));

        await app.Sender.Send(new CancelOrderCommand(order.Id, "late and wrong"));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task A_payment_for_an_already_cancelled_order_is_not_confirmed()
    {
        var (app, order) = AppWithOrder();
        await app.Sender.Send(new CancelOrderCommand(order.Id, "Not enough stock"));

        await app.Sender.Send(new ConfirmOrderPaymentCommand(order.Id));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(app.Events.Confirmed);
    }

    [Fact]
    public async Task Events_for_unknown_orders_are_dropped_without_error()
    {
        var app = new TestApp();
        var unknown = Guid.NewGuid();

        await app.Sender.Send(new MarkOrderStockReservedCommand(unknown));
        await app.Sender.Send(new CancelOrderCommand(unknown, "x"));
        await app.Sender.Send(new ConfirmOrderPaymentCommand(unknown));

        Assert.Equal(0, app.UnitOfWork.Saves);
    }
}

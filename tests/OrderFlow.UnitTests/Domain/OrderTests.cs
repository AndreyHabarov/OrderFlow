using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() => Order.Create(
        Guid.NewGuid(),
        [new OrderItem(Guid.NewGuid(), "Keyboard", 2, new Money(50m))],
        Now);

    [Fact]
    public void Create_starts_pending_and_sums_total()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(100m, order.Total.Amount);
    }

    [Fact]
    public void Create_without_items_is_rejected()
    {
        Assert.Throws<DomainException>(() => Order.Create(Guid.NewGuid(), [], Now));
    }

    [Fact]
    public void Happy_path_walks_through_all_statuses()
    {
        var order = NewOrder();

        order.MarkStockReserved(Now);
        order.MarkPaid(Now);
        order.Confirm(Now);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Cannot_skip_a_status()
    {
        var order = NewOrder();

        Assert.Throws<DomainException>(() => order.MarkPaid(Now));
        Assert.Throws<DomainException>(() => order.Confirm(Now));
    }

    [Fact]
    public void Cancel_records_reason_and_blocks_further_transitions()
    {
        var order = NewOrder();

        order.Cancel("payment declined", Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("payment declined", order.CancellationReason);
        Assert.Throws<DomainException>(() => order.MarkStockReserved(Now));
        Assert.Throws<DomainException>(() => order.Cancel("again", Now));
    }

    [Fact]
    public void Confirmed_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.MarkStockReserved(Now);
        order.MarkPaid(Now);
        order.Confirm(Now);

        Assert.Throws<DomainException>(() => order.Cancel("too late", Now));
    }
}

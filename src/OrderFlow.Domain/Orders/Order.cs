using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
        CancellationReason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string CancellationReason { get; private set; }

    /// <summary>Client-supplied key that makes order creation safe to retry. Unique per customer when present.</summary>
    public string? IdempotencyKey { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    public Money Total => _items.Aggregate(Money.Zero(), (sum, item) => sum.Add(item.LineTotal));

    public static Order Create(Guid customerId, IEnumerable<OrderItem> items, DateTimeOffset now, string? idempotencyKey = null)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainException("Customer id is required.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            IdempotencyKey = idempotencyKey
        };
        order._items.AddRange(items);

        if (order._items.Count == 0)
        {
            throw new DomainException("An order must contain at least one item.");
        }

        return order;
    }

    public void MarkStockReserved(DateTimeOffset now) => Transition(OrderStatus.Pending, OrderStatus.StockReserved, now);

    public void MarkPaid(DateTimeOffset now) => Transition(OrderStatus.StockReserved, OrderStatus.Paid, now);

    public void Confirm(DateTimeOffset now) => Transition(OrderStatus.Paid, OrderStatus.Confirmed, now);

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled)
        {
            throw new DomainException($"An order in status {Status} cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = now;
    }

    private void Transition(OrderStatus expected, OrderStatus next, DateTimeOffset now)
    {
        if (Status != expected)
        {
            throw new DomainException($"Cannot move order from {Status} to {next}: expected {expected}.");
        }

        Status = next;
        UpdatedAt = now;
    }
}

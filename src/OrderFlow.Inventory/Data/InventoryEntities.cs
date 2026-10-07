namespace OrderFlow.Inventory.Data;

/// <summary>How many units of a product can still be promised to customers.</summary>
public sealed class StockItem
{
    private StockItem()
    {
    }

    public Guid ProductId { get; private set; }

    public int Available { get; private set; }

    public static StockItem Create(Guid productId, int available) => new() { ProductId = productId, Available = available };

    /// <summary>Caller must have checked <see cref="Available"/>; going negative is a bug, not a business outcome.</summary>
    public void Take(int quantity)
    {
        if (quantity <= 0 || quantity > Available)
        {
            throw new InvalidOperationException($"Cannot take {quantity} from {Available} available.");
        }

        Available -= quantity;
    }
}

public enum ReservationStatus
{
    Reserved = 0,
    Failed = 1
}

/// <summary>
/// The outcome of handling one order, keyed by order id. Because it is saved in the same transaction as the stock
/// change, a redelivered <c>OrderCreated</c> finds it and only repeats the answer instead of reserving twice.
/// It also stores what is needed to rebuild the answer (customer, total) without asking Orders again.
/// </summary>
public sealed class Reservation
{
    private readonly List<ReservationLine> _lines = [];

    private Reservation()
    {
        Currency = string.Empty;
    }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; }

    public ReservationStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<ReservationLine> Lines => _lines;

    public static Reservation Reserved(Guid orderId, Guid customerId, decimal totalAmount, string currency, IEnumerable<ReservationLine> lines, DateTimeOffset now)
    {
        var reservation = Create(orderId, customerId, totalAmount, currency, ReservationStatus.Reserved, null, now);
        reservation._lines.AddRange(lines);
        return reservation;
    }

    public static Reservation Rejected(Guid orderId, Guid customerId, decimal totalAmount, string currency, string reason, DateTimeOffset now) =>
        Create(orderId, customerId, totalAmount, currency, ReservationStatus.Failed, reason, now);

    private static Reservation Create(Guid orderId, Guid customerId, decimal totalAmount, string currency, ReservationStatus status, string? reason, DateTimeOffset now) => new()
    {
        OrderId = orderId,
        CustomerId = customerId,
        TotalAmount = totalAmount,
        Currency = currency,
        Status = status,
        FailureReason = reason,
        CreatedAt = now
    };
}

public sealed record ReservationLine(Guid ProductId, int Quantity)
{
    // Used by EF Core when materializing rows.
    private ReservationLine() : this(Guid.Empty, 0)
    {
    }
}

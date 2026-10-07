using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Orders;

/// <summary>Snapshot of a product at order time: later price or name changes do not affect the order.</summary>
public sealed record OrderItem(Guid ProductId, string ProductName, int Quantity, Money UnitPrice)
{
    // Used by EF Core when materializing rows.
    private OrderItem() : this(Guid.Empty, string.Empty, 0, default)
    {
    }

    public Money LineTotal => UnitPrice.Multiply(Quantity);
}

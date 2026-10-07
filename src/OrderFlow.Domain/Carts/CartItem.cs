using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Carts;

public sealed record CartItem(Guid ProductId, int Quantity, Money UnitPrice)
{
    // Used by EF Core when materializing rows.
    private CartItem() : this(Guid.Empty, 0, default)
    {
    }

    public Money LineTotal => UnitPrice.Multiply(Quantity);
}

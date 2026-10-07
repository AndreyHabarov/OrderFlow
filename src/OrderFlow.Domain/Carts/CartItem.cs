using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Carts;

public sealed record CartItem(Guid ProductId, int Quantity, Money UnitPrice)
{
    public Money LineTotal => UnitPrice.Multiply(Quantity);
}

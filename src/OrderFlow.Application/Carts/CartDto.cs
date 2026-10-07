using OrderFlow.Domain.Carts;

namespace OrderFlow.Application.Carts;

public sealed record CartItemDto(Guid ProductId, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record CartDto(IReadOnlyList<CartItemDto> Items, decimal Total, string Currency)
{
    public static CartDto Empty { get; } = new([], 0m, "USD");

    public static CartDto From(Cart cart) => new(
        cart.Items.Select(i => new CartItemDto(i.ProductId, i.Quantity, i.UnitPrice.Amount, i.LineTotal.Amount)).ToList(),
        cart.Total.Amount,
        cart.Total.Currency);
}

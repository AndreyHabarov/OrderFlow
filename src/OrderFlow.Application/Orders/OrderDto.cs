using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

public sealed record OrderItemDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt,
    string? CancellationReason,
    IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.Total.Amount,
        order.Total.Currency,
        order.CreatedAt,
        string.IsNullOrEmpty(order.CancellationReason) ? null : order.CancellationReason,
        order.Items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice.Amount, i.LineTotal.Amount)).ToList());
}

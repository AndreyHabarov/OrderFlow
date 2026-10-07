using MediatR;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Catalog;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

/// <summary>
/// Turns the customer's cart into an order. In stage 1 everything is synchronous: stock is reserved in the same
/// database transaction that creates the order and empties the cart, so the order ends in <c>StockReserved</c>.
/// Stage 2 replaces the stock step with messages.
/// </summary>
public sealed record CheckoutCommand : IRequest<OrderDto>;

internal sealed class CheckoutCommandHandler(
    ICartRepository carts,
    IProductRepository products,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CheckoutCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CheckoutCommand request, CancellationToken cancellationToken)
    {
        var customerId = currentUser.CustomerId;
        var cart = await carts.GetByCustomerAsync(customerId, cancellationToken);
        if (cart is null || cart.Items.Count == 0)
        {
            throw new DomainException("The cart is empty.");
        }

        var ids = cart.Items.Select(i => i.ProductId).ToList();
        var loaded = (await products.GetTrackedByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        var orderItems = new List<OrderItem>(cart.Items.Count);
        foreach (var item in cart.Items)
        {
            if (!loaded.TryGetValue(item.ProductId, out var product))
            {
                throw new DomainException($"Product {item.ProductId} is no longer available.");
            }

            product.Reserve(item.Quantity);
            orderItems.Add(new OrderItem(product.Id, product.Name, item.Quantity, product.Price));
        }

        var now = timeProvider.GetUtcNow();
        var order = Order.Create(customerId, orderItems, now);
        order.MarkStockReserved(now);

        orders.Add(order);
        cart.Clear();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Stock changed, so cached catalog pages are stale.
        await cache.InvalidateNamespaceAsync(CatalogCache.Namespace, cancellationToken);

        return OrderDto.From(order);
    }
}

public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>;

internal sealed class GetOrderQueryHandler(IOrderRepository orders, ICurrentUser currentUser) : IRequestHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.GetForCustomerAsync(request.Id, currentUser.CustomerId, cancellationToken)
                    ?? throw new NotFoundException($"Order {request.Id} was not found.");
        return OrderDto.From(order);
    }
}

public sealed record GetMyOrdersQuery : IRequest<IReadOnlyList<OrderDto>>;

internal sealed class GetMyOrdersQueryHandler(IOrderRepository orders, ICurrentUser currentUser)
    : IRequestHandler<GetMyOrdersQuery, IReadOnlyList<OrderDto>>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken)
    {
        var list = await orders.GetByCustomerAsync(currentUser.CustomerId, cancellationToken);
        return list.Select(OrderDto.From).ToList();
    }
}

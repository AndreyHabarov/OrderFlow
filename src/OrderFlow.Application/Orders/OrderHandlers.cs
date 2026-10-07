using FluentValidation;
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
/// The command is idempotent: the same <see cref="IdempotencyKey"/> from the same customer never creates a second order.
/// </summary>
public sealed record CheckoutCommand(string IdempotencyKey) : IRequest<CheckoutResult>;

/// <summary><see cref="Replayed"/> is true when the order already existed for this idempotency key.</summary>
public sealed record CheckoutResult(OrderDto Order, bool Replayed);

internal sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey)
            .NotEmpty().WithMessage("The Idempotency-Key header is required.")
            .MaximumLength(100);
    }
}

internal sealed class CheckoutCommandHandler(
    ICartRepository carts,
    IProductRepository products,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CheckoutCommand, CheckoutResult>
{
    public async Task<CheckoutResult> Handle(CheckoutCommand request, CancellationToken cancellationToken)
    {
        var customerId = currentUser.CustomerId;

        // Fast path: a retry of a request that already succeeded.
        if (await FindReplayAsync(customerId, request.IdempotencyKey, cancellationToken) is { } replay)
        {
            return replay;
        }

        var cart = await carts.GetByCustomerAsync(customerId, cancellationToken);
        if (cart is null || cart.Items.Count == 0)
        {
            // An empty cart can also mean a concurrent request with the same key just committed and emptied it
            // after our fast-path lookup. Look again before reporting an error.
            return await FindReplayAsync(customerId, request.IdempotencyKey, cancellationToken)
                   ?? throw new DomainException("The cart is empty.");
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
        var order = Order.Create(customerId, orderItems, now, request.IdempotencyKey);
        order.MarkStockReserved(now);

        orders.Add(order);
        cart.Clear();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (unitOfWork.IsUniqueViolation(ex) || unitOfWork.IsConcurrencyConflict(ex))
        {
            // We lost a race. If the winner is a request with the same idempotency key (a duplicate), our whole
            // transaction (including the stock reservation) was rolled back and we return the winner's order.
            // The race shows up as a unique violation on the key, or as a version conflict on the product rows the
            // winner already updated, depending on which statement collides first.
            var winner = await FindReplayAsync(customerId, request.IdempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            // No such order: a different customer took the stock first. Let the API answer 409 so the client can retry.
            throw;
        }

        // Stock changed, so cached catalog pages are stale.
        await cache.InvalidateNamespaceAsync(CatalogCache.Namespace, cancellationToken);

        return new CheckoutResult(OrderDto.From(order), Replayed: false);
    }

    private async Task<CheckoutResult?> FindReplayAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var existing = await orders.GetByIdempotencyKeyAsync(customerId, idempotencyKey, cancellationToken);
        return existing is null ? null : new CheckoutResult(OrderDto.From(existing), Replayed: true);
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

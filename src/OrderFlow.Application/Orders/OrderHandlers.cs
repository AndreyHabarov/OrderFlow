using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

/// <summary>
/// Turns the customer's cart into an order in status <c>Pending</c> and announces it (<c>OrderCreated</c>). From
/// there the other services take over: Inventory reserves stock, Payments charges, and the events they publish move
/// the order forward (see <see cref="OrderEventCommands"/>). The response is therefore "accepted", not "done".
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

internal sealed partial class CheckoutCommandHandler(
    ICartRepository carts,
    IProductRepository products,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IOrderEventPublisher events,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<CheckoutCommandHandler> logger) : IRequestHandler<CheckoutCommand, CheckoutResult>
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
        var catalog = (await products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        // The order keeps a snapshot of name and price; availability is Inventory's business, decided later.
        var orderItems = new List<OrderItem>(cart.Items.Count);
        foreach (var item in cart.Items)
        {
            if (!catalog.TryGetValue(item.ProductId, out var product))
            {
                throw new DomainException($"Product {item.ProductId} is no longer available.");
            }

            orderItems.Add(new OrderItem(product.Id, product.Name, item.Quantity, product.Price));
        }

        var now = timeProvider.GetUtcNow();
        var order = Order.Create(customerId, orderItems, now, request.IdempotencyKey);

        orders.Add(order);
        cart.Clear();

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (unitOfWork.IsUniqueViolation(ex) || unitOfWork.IsConcurrencyConflict(ex))
        {
            // We lost a race. If the winner is a request with the same idempotency key (a duplicate), our whole
            // transaction was rolled back and we return the winner's order. The race shows up as a unique violation
            // on the key or as a version conflict, depending on which statement collides first.
            var winner = await FindReplayAsync(customerId, request.IdempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }

        // The order is committed. Announce it; if the broker is unavailable the order stays Pending until stage 3's
        // outbox makes publishing reliable (ADR 0006, known gap 1). The customer's request is not failed for it.
        try
        {
            await events.OrderCreatedAsync(order, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublishFailed(ex, order.Id);
        }

        return new CheckoutResult(OrderDto.From(order), Replayed: false);
    }

    private async Task<CheckoutResult?> FindReplayAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var existing = await orders.GetByIdempotencyKeyAsync(customerId, idempotencyKey, cancellationToken);
        return existing is null ? null : new CheckoutResult(OrderDto.From(existing), Replayed: true);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order {OrderId} was saved but OrderCreated could not be published; it stays Pending")]
    private partial void LogPublishFailed(Exception exception, Guid orderId);
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

using MediatR;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Orders;

// Commands raised by events from Inventory and Payments. They are written to be safe under at-least-once delivery:
// a repeated or late event never throws and never moves an order backwards. An event for an unknown order is logged
// and dropped (retrying cannot create the order).

/// <summary>Inventory reserved the stock.</summary>
public sealed record MarkOrderStockReservedCommand(Guid OrderId) : IRequest;

/// <summary>Inventory or Payments refused: the order is cancelled with the reason (shown to the customer).</summary>
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : IRequest;

/// <summary>Payments charged the customer: the order is paid and confirmed.</summary>
public sealed record ConfirmOrderPaymentCommand(Guid OrderId) : IRequest;

internal sealed partial class MarkOrderStockReservedHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<MarkOrderStockReservedHandler> logger) : IRequestHandler<MarkOrderStockReservedCommand>
{
    public async Task Handle(MarkOrderStockReservedCommand request, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            LogUnknownOrder(request.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            LogIgnored(order.Id, order.Status); // duplicate, or the order already moved on / was cancelled
            return;
        }

        order.MarkStockReserved(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "StockReserved for unknown order {OrderId}; dropped")]
    private partial void LogUnknownOrder(Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "StockReserved ignored for order {OrderId} in status {Status}")]
    private partial void LogIgnored(Guid orderId, OrderStatus status);
}

internal sealed partial class CancelOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CancelOrderHandler> logger) : IRequestHandler<CancelOrderCommand>
{
    public async Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            LogUnknownOrder(request.OrderId);
            return;
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Confirmed or OrderStatus.Paid)
        {
            LogIgnored(order.Id, order.Status);
            return;
        }

        order.Cancel(request.Reason, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cancellation for unknown order {OrderId}; dropped")]
    private partial void LogUnknownOrder(Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancellation ignored for order {OrderId} in status {Status}")]
    private partial void LogIgnored(Guid orderId, OrderStatus status);
}

internal sealed partial class ConfirmOrderPaymentHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IOrderEventPublisher events,
    TimeProvider timeProvider,
    ILogger<ConfirmOrderPaymentHandler> logger) : IRequestHandler<ConfirmOrderPaymentCommand>
{
    public async Task Handle(ConfirmOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            LogUnknownOrder(request.OrderId);
            return;
        }

        switch (order.Status)
        {
            case OrderStatus.Cancelled:
                // Money was taken for an order that was already cancelled. Refunds do not exist yet (stage 3 saga).
                LogPaidButCancelled(order.Id);
                return;

            case OrderStatus.Confirmed:
                // A repeat: the previous attempt may have died before announcing the confirmation, so announce again.
                await events.OrderConfirmedAsync(order, cancellationToken);
                return;
        }

        var now = timeProvider.GetUtcNow();
        if (order.Status == OrderStatus.Pending)
        {
            order.MarkStockReserved(now); // a payment proves the stock was reserved, even if its own event is late
        }

        order.MarkPaid(now);
        order.Confirm(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.OrderConfirmedAsync(order, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PaymentSucceeded for unknown order {OrderId}; dropped")]
    private partial void LogUnknownOrder(Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Order {OrderId} was paid but is already cancelled; a refund is needed")]
    private partial void LogPaidButCancelled(Guid orderId);
}

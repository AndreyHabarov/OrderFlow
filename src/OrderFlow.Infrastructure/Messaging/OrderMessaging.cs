using MediatR;
using Microsoft.AspNetCore.Http;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;
using OrderFlow.Domain.Orders;
using OrderFlow.Messaging;

namespace OrderFlow.Infrastructure.Messaging;

/// <summary>
/// Carries the context of the message being handled to the publisher in the same scope, so events published while
/// handling a message keep its correlation id and name it as their cause.
/// </summary>
internal sealed class MessageContextAccessor
{
    public MessageContext? Current { get; set; }
}

/// <summary>Maps orders to contract messages and publishes them. Correlation comes from the message being handled or from the HTTP request.</summary>
internal sealed class OrderEventPublisher(IMessagePublisher publisher, MessageContextAccessor messageContext, IHttpContextAccessor httpContext) : IOrderEventPublisher
{
    public Task OrderCreatedAsync(Order order, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            new OrderCreated(
                order.Id,
                order.CustomerId,
                order.Items.Select(item => new OrderLine(item.ProductId, item.Quantity)).ToList(),
                order.Total.Amount,
                order.Total.Currency),
            CurrentCause(),
            cancellationToken);

    public Task OrderConfirmedAsync(Order order, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new OrderConfirmed(order.Id, order.CustomerId), CurrentCause(), cancellationToken);

    private MessageContext? CurrentCause()
    {
        if (messageContext.Current is { } handling)
        {
            return handling;
        }

        // Started by an HTTP request: reuse its correlation id so logs and messages line up.
        var correlationId = httpContext.HttpContext?.Response.Headers["X-Correlation-Id"].ToString();
        return string.IsNullOrEmpty(correlationId) ? null : MessageContext.ForNewFlow(correlationId);
    }
}

// Thin adapters from broker messages to application commands: all decisions live in the Application layer.

internal sealed class StockReservedConsumer(ISender sender, MessageContextAccessor accessor) : IMessageHandler<StockReserved>
{
    public Task HandleAsync(StockReserved message, MessageContext context, CancellationToken cancellationToken)
    {
        accessor.Current = context;
        return sender.Send(new MarkOrderStockReservedCommand(message.OrderId), cancellationToken);
    }
}

internal sealed class StockReservationFailedConsumer(ISender sender, MessageContextAccessor accessor) : IMessageHandler<StockReservationFailed>
{
    public Task HandleAsync(StockReservationFailed message, MessageContext context, CancellationToken cancellationToken)
    {
        accessor.Current = context;
        return sender.Send(new CancelOrderCommand(message.OrderId, message.Reason), cancellationToken);
    }
}

internal sealed class PaymentSucceededConsumer(ISender sender, MessageContextAccessor accessor) : IMessageHandler<PaymentSucceeded>
{
    public Task HandleAsync(PaymentSucceeded message, MessageContext context, CancellationToken cancellationToken)
    {
        accessor.Current = context;
        return sender.Send(new ConfirmOrderPaymentCommand(message.OrderId), cancellationToken);
    }
}

internal sealed class PaymentFailedConsumer(ISender sender, MessageContextAccessor accessor) : IMessageHandler<PaymentFailed>
{
    public Task HandleAsync(PaymentFailed message, MessageContext context, CancellationToken cancellationToken)
    {
        accessor.Current = context;
        return sender.Send(new CancelOrderCommand(message.OrderId, $"Payment failed: {message.Reason}"), cancellationToken);
    }
}

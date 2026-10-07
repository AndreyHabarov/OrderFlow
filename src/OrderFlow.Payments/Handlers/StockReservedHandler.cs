using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderFlow.Contracts;
using OrderFlow.Messaging;
using OrderFlow.Payments.Data;

namespace OrderFlow.Payments.Handlers;

/// <summary>
/// Charges the customer (emulated) once stock is reserved and answers with <see cref="PaymentSucceeded"/> or
/// <see cref="PaymentFailed"/>. Same shape as the Inventory handler: decide and commit, then publish; a redelivered
/// message finds the stored payment and repeats the answer, so a customer is never charged twice.
/// </summary>
internal sealed partial class StockReservedHandler(
    PaymentsDbContext db,
    IPaymentModeProvider modes,
    IMessagePublisher publisher,
    TimeProvider timeProvider,
    IOptions<PaymentOptions> options,
    ILogger<StockReservedHandler> logger) : IMessageHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved message, MessageContext context, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == message.OrderId, cancellationToken);
        if (payment is null)
        {
            payment = await ChargeAsync(message, cancellationToken);
        }
        else
        {
            LogRepeated(message.OrderId, payment.Status);
        }

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await publisher.PublishAsync(new PaymentSucceeded(payment.OrderId, payment.Amount, payment.Currency), context, cancellationToken);
        }
        else
        {
            await publisher.PublishAsync(new PaymentFailed(payment.OrderId, payment.FailureReason ?? "Payment failed"), context, cancellationToken);
        }
    }

    private async Task<Payment> ChargeAsync(StockReserved message, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var mode = await modes.GetAsync(cancellationToken);

        Payment payment;
        switch (mode)
        {
            case PaymentMode.Decline:
                payment = Payment.Failed(message.OrderId, message.CustomerId, message.TotalAmount, message.Currency, "The bank declined the payment.", now);
                break;
            case PaymentMode.Timeout:
                // The emulated bank is slow; the message stays unacknowledged while we wait.
                await Task.Delay(options.Value.TimeoutDelay, cancellationToken);
                payment = Payment.Failed(message.OrderId, message.CustomerId, message.TotalAmount, message.Currency, "The payment timed out.", timeProvider.GetUtcNow());
                break;
            default:
                payment = Payment.Succeeded(message.OrderId, message.CustomerId, message.TotalAmount, message.Currency, now);
                break;
        }

        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            LogCharged(payment.OrderId, mode, payment.Status);
            return payment;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent delivery of the same order saved its payment first; use that one.
            db.ChangeTracker.Clear();
            return await db.Payments.AsNoTracking().SingleAsync(p => p.OrderId == payment.OrderId, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: payment in mode {Mode} -> {Status}")]
    private partial void LogCharged(Guid orderId, PaymentMode mode, PaymentStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: already processed ({Status}); repeating the answer")]
    private partial void LogRepeated(Guid orderId, PaymentStatus status);
}

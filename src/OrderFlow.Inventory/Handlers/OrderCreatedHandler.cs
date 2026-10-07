using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderFlow.Contracts;
using OrderFlow.Inventory.Data;
using OrderFlow.Messaging;

namespace OrderFlow.Inventory.Handlers;

/// <summary>
/// Reserves stock for an order and answers with <see cref="StockReserved"/> or <see cref="StockReservationFailed"/>.
///
/// Order of work: decide and commit (stock change + reservation record in ONE transaction), then publish, then the
/// consumer acknowledges. If anything fails after the commit, the message is redelivered, the handler finds the
/// reservation by order id and publishes the same answer again. So duplicates and crashes never reserve twice, and
/// a lost answer is recovered by redelivery. Downstream services must tolerate the repeated answer.
/// </summary>
internal sealed partial class OrderCreatedHandler(
    InventoryDbContext db,
    IMessagePublisher publisher,
    TimeProvider timeProvider,
    ILogger<OrderCreatedHandler> logger) : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.OrderId == message.OrderId, cancellationToken);
        if (reservation is null)
        {
            reservation = await ReserveAsync(message, cancellationToken);
        }
        else
        {
            LogRepeated(message.OrderId, reservation.Status);
        }

        if (reservation.Status == ReservationStatus.Reserved)
        {
            await publisher.PublishAsync(
                new StockReserved(reservation.OrderId, reservation.CustomerId, reservation.TotalAmount, reservation.Currency),
                context,
                cancellationToken);
        }
        else
        {
            await publisher.PublishAsync(new StockReservationFailed(reservation.OrderId, reservation.FailureReason ?? "Reservation failed"), context, cancellationToken);
        }
    }

    private async Task<Reservation> ReserveAsync(OrderCreated message, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var wanted = message.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        if (wanted.Count == 0 || wanted.Values.Any(quantity => quantity <= 0))
        {
            return await SaveAsync(Reservation.Rejected(message.OrderId, message.CustomerId, message.TotalAmount, message.Currency, "The order has no valid lines.", now), cancellationToken);
        }

        var productIds = wanted.Keys.ToList();
        var items = await db.StockItems.Where(item => productIds.Contains(item.ProductId)).ToDictionaryAsync(item => item.ProductId, cancellationToken);

        foreach (var (productId, quantity) in wanted)
        {
            if (!items.TryGetValue(productId, out var item))
            {
                return await SaveAsync(Reservation.Rejected(message.OrderId, message.CustomerId, message.TotalAmount, message.Currency, $"Unknown product {productId}.", now), cancellationToken);
            }

            if (item.Available < quantity)
            {
                var reason = string.Create(CultureInfo.InvariantCulture, $"Not enough stock for product {productId}: requested {quantity}, available {item.Available}.");
                return await SaveAsync(Reservation.Rejected(message.OrderId, message.CustomerId, message.TotalAmount, message.Currency, reason, now), cancellationToken);
            }
        }

        foreach (var (productId, quantity) in wanted)
        {
            items[productId].Take(quantity);
        }

        var reserved = Reservation.Reserved(
            message.OrderId,
            message.CustomerId,
            message.TotalAmount,
            message.Currency,
            wanted.Select(pair => new ReservationLine(pair.Key, pair.Value)),
            now);
        return await SaveAsync(reserved, cancellationToken);
    }

    private async Task<Reservation> SaveAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        db.Reservations.Add(reservation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            LogDecided(reservation.OrderId, reservation.Status, reservation.FailureReason);
            return reservation;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent delivery of the same order committed first (its stock change is already applied).
            // Our transaction rolled back; use the winner's decision.
            db.ChangeTracker.Clear();
            return await db.Reservations.AsNoTracking().SingleAsync(r => r.OrderId == reservation.OrderId, cancellationToken);
        }

        // A DbUpdateConcurrencyException (another consumer changed the same stock row) is not caught on purpose:
        // the consumer requeues the message and the next attempt reads the new stock level.
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: reservation {Status} ({Reason})")]
    private partial void LogDecided(Guid orderId, ReservationStatus status, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: already decided ({Status}); repeating the answer")]
    private partial void LogRepeated(Guid orderId, ReservationStatus status);
}

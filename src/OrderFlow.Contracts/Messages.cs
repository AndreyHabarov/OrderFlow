namespace OrderFlow.Contracts;

// Every message identifies its order, so a whole business flow can be followed by OrderId (and by the
// CorrelationId carried in the AMQP properties). Amounts use decimal + ISO currency like the Orders API.

public sealed record OrderLine(Guid ProductId, int Quantity);

/// <summary>Orders -> Inventory. A customer placed an order; stock must be reserved.</summary>
[MessageType("order.created")]
public sealed record OrderCreated(Guid OrderId, Guid CustomerId, IReadOnlyList<OrderLine> Lines, decimal TotalAmount, string Currency);

/// <summary>Inventory -> Payments, Orders. Every line of the order was reserved. Carries what Payments needs to charge.</summary>
[MessageType("stock.reserved")]
public sealed record StockReserved(Guid OrderId, Guid CustomerId, decimal TotalAmount, string Currency);

/// <summary>Inventory -> Orders. The reservation was refused (unknown product or not enough stock); nothing was reserved.</summary>
[MessageType("stock.reservation-failed")]
public sealed record StockReservationFailed(Guid OrderId, string Reason);

/// <summary>Payments -> Orders. The customer was charged.</summary>
[MessageType("payment.succeeded")]
public sealed record PaymentSucceeded(Guid OrderId, decimal Amount, string Currency);

/// <summary>Payments -> Orders. The charge was refused (bank decline, timeout, ...).</summary>
[MessageType("payment.failed")]
public sealed record PaymentFailed(Guid OrderId, string Reason);

/// <summary>Orders -> anyone interested. The order is paid and confirmed (no consumer yet; notifications could use it).</summary>
[MessageType("order.confirmed")]
public sealed record OrderConfirmed(Guid OrderId, Guid CustomerId);

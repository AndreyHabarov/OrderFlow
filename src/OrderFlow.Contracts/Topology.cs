namespace OrderFlow.Contracts;

/// <summary>
/// Where messages travel: one durable topic exchange and one durable queue per consuming service.
/// Publishers only know the exchange and a routing key; each service binds its queue to the keys it handles.
/// </summary>
public static class Topology
{
    public const string EventsExchange = "orderflow.events";

    public sealed record QueueDefinition(string Name, IReadOnlyList<string> RoutingKeys);

    public static QueueDefinition Orders { get; } = new("orders.events",
    [
        MessageCatalog.RoutingKeyOf<StockReserved>(),
        MessageCatalog.RoutingKeyOf<StockReservationFailed>(),
        MessageCatalog.RoutingKeyOf<PaymentSucceeded>(),
        MessageCatalog.RoutingKeyOf<PaymentFailed>()
    ]);

    public static QueueDefinition Inventory { get; } = new("inventory.events",
    [
        MessageCatalog.RoutingKeyOf<OrderCreated>()
    ]);

    public static QueueDefinition Payments { get; } = new("payments.events",
    [
        MessageCatalog.RoutingKeyOf<StockReserved>()
    ]);

    public static IReadOnlyList<QueueDefinition> All { get; } = [Orders, Inventory, Payments];
}

using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Abstractions;

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Read-only lookup of several products (to snapshot name and price into an order).</summary>
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public interface ICartRepository
{
    /// <summary>Tracked cart with its items, or null when the customer has none.</summary>
    Task<Cart?> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    void Add(Cart cart);
}

public interface IOrderRepository
{
    void Add(Order order);

    /// <summary>The customer's order created with this idempotency key, if any (read-only).</summary>
    Task<Order?> GetByIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Tracked order by id regardless of owner, for the handlers of events from other services.</summary>
    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Returns the order only if it belongs to the customer.</summary>
    Task<Order?> GetForCustomerAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}

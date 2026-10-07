using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Abstractions;

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads products with change tracking so their stock can be modified in the current unit of work.</summary>
    Task<IReadOnlyList<Product>> GetTrackedByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
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

    /// <summary>Returns the order only if it belongs to the customer.</summary>
    Task<Order?> GetForCustomerAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Abstractions;
using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Orders;

namespace OrderFlow.Infrastructure.Persistence.Repositories;

internal sealed class CartRepository(AppDbContext db) : ICartRepository
{
    public Task<Cart?> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

    public void Add(Cart cart) => db.Carts.Add(cart);
}

internal sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task<Order?> GetForCustomerAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
}

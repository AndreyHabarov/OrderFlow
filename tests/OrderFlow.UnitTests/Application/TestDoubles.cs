using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Orders;

namespace OrderFlow.UnitTests.Application;

internal sealed class FakeProducts(params Product[] products) : IProductRepository
{
    public int Calls { get; private set; }

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        Calls++;
        IReadOnlyList<Product> items = products.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, products.Length));
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(products.FirstOrDefault(p => p.Id == id));
    }

    public Task<IReadOnlyList<Product>> GetTrackedByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> found = products.Where(p => ids.Contains(p.Id)).ToList();
        return Task.FromResult(found);
    }
}

internal sealed class FakeCarts : ICartRepository
{
    public List<Cart> Store { get; } = [];

    public Task<Cart?> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Store.FirstOrDefault(c => c.CustomerId == customerId));

    public void Add(Cart cart) => Store.Add(cart);
}

internal sealed class FakeOrders : IOrderRepository
{
    public List<Order> Store { get; } = [];

    public void Add(Order order) => Store.Add(order);

    public Task<Order?> GetByIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Store.FirstOrDefault(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey));

    public Task<Order?> GetForCustomerAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Store.FirstOrDefault(o => o.Id == orderId && o.CustomerId == customerId));

    public Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Order> list = Store.Where(o => o.CustomerId == customerId).ToList();
        return Task.FromResult(list);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    /// <summary>Runs inside the next save and may throw, e.g. to simulate a concurrent duplicate insert.</summary>
    public Action? OnNextSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (OnNextSave is { } hook)
        {
            OnNextSave = null;
            hook();
        }

        Saves++;
        return Task.FromResult(1);
    }

    public bool IsUniqueViolation(Exception exception) => exception is UniqueViolationSimulated;
}

internal sealed class FakeCurrentUser(Guid customerId) : ICurrentUser
{
    public Guid CustomerId { get; } = customerId;
}

/// <summary>In-memory cache that keeps values per key and namespace version, like Redis would.</summary>
internal sealed class FakeCache : ICacheService
{
    private readonly Dictionary<string, object> _store = [];

    public int Invalidations { get; private set; }

    public async Task<T?> GetOrCreateAsync<T>(
        string cacheNamespace,
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
        where T : class
    {
        var fullKey = $"{cacheNamespace}:{Invalidations}:{key}";
        if (_store.TryGetValue(fullKey, out var cached))
        {
            return (T)cached;
        }

        var created = await factory(cancellationToken);
        if (created is not null)
        {
            _store[fullKey] = created;
        }

        return created;
    }

    public Task InvalidateNamespaceAsync(string cacheNamespace, CancellationToken cancellationToken)
    {
        Invalidations++;
        return Task.CompletedTask;
    }
}

/// <summary>Builds the real MediatR pipeline (validation included) on top of the fakes.</summary>
internal sealed class TestApp
{
    public TestApp(Guid? customerId = null, params Product[] products)
    {
        CustomerId = customerId ?? Guid.NewGuid();
        Products = new FakeProducts(products);

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IProductRepository>(Products);
        services.AddSingleton<ICartRepository>(Carts);
        services.AddSingleton<IOrderRepository>(Orders);
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton<ICacheService>(Cache);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser(CustomerId));
        Sender = services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    public Guid CustomerId { get; }

    public FakeProducts Products { get; }

    public FakeCarts Carts { get; } = new();

    public FakeOrders Orders { get; } = new();

    public FakeUnitOfWork UnitOfWork { get; } = new();

    public FakeCache Cache { get; } = new();

    public ISender Sender { get; }

    public static Product Product(string name, decimal price = 10m, int stock = 5) =>
        OrderFlow.Domain.Catalog.Product.Create(name, "d", new Money(price), stock);
}

internal sealed class UniqueViolationSimulated : Exception
{
}

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Catalog;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;

namespace OrderFlow.UnitTests.Application;

public class CatalogQueryTests
{
    private sealed class FakeProducts(params Product[] products) : IProductRepository
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
    }

    /// <summary>In-memory cache that keeps values per key, like Redis would.</summary>
    private sealed class FakeCache : ICacheService
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

    private static ISender BuildSender(FakeProducts products, FakeCache cache)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IProductRepository>(products);
        services.AddSingleton<ICacheService>(cache);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static Product Sample(string name) => Product.Create(name, "d", new Money(10m), 5);

    [Fact]
    public async Task List_is_served_from_cache_on_the_second_call()
    {
        var products = new FakeProducts(Sample("A"), Sample("B"));
        var sender = BuildSender(products, new FakeCache());

        var first = await sender.Send(new GetProductsQuery(1, 10));
        var second = await sender.Send(new GetProductsQuery(1, 10));

        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(1, products.Calls);
    }

    [Fact]
    public async Task Invalidating_the_namespace_forces_a_reload()
    {
        var products = new FakeProducts(Sample("A"));
        var cache = new FakeCache();
        var sender = BuildSender(products, cache);

        await sender.Send(new GetProductsQuery());
        await cache.InvalidateNamespaceAsync(CatalogCache.Namespace, CancellationToken.None);
        await sender.Send(new GetProductsQuery());

        Assert.Equal(2, products.Calls);
    }

    [Fact]
    public async Task Page_size_is_clamped_and_page_is_at_least_one()
    {
        var products = new FakeProducts(Sample("A"));
        var sender = BuildSender(products, new FakeCache());

        var result = await sender.Send(new GetProductsQuery(Page: -5, PageSize: 10_000));

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task Missing_product_returns_null_and_is_not_cached()
    {
        var products = new FakeProducts();
        var sender = BuildSender(products, new FakeCache());
        var id = Guid.NewGuid();

        Assert.Null(await sender.Send(new GetProductQuery(id)));
        Assert.Null(await sender.Send(new GetProductQuery(id)));

        Assert.Equal(2, products.Calls);
    }

    [Fact]
    public async Task Existing_product_is_mapped_to_dto()
    {
        var product = Sample("Keyboard");
        var sender = BuildSender(new FakeProducts(product), new FakeCache());

        var dto = await sender.Send(new GetProductQuery(product.Id));

        Assert.NotNull(dto);
        Assert.Equal("Keyboard", dto.Name);
        Assert.Equal(10m, dto.Price);
        Assert.Equal("USD", dto.Currency);
    }
}

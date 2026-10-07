using OrderFlow.Application.Catalog;

namespace OrderFlow.UnitTests.Application;

public class CatalogQueryTests
{
    [Fact]
    public async Task List_is_served_from_cache_on_the_second_call()
    {
        var app = new TestApp(null, TestApp.Product("A"), TestApp.Product("B"));

        var first = await app.Sender.Send(new GetProductsQuery(1, 10));
        var second = await app.Sender.Send(new GetProductsQuery(1, 10));

        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(1, app.Products.Calls);
    }

    [Fact]
    public async Task Invalidating_the_namespace_forces_a_reload()
    {
        var app = new TestApp(null, TestApp.Product("A"));

        await app.Sender.Send(new GetProductsQuery());
        await app.Cache.InvalidateNamespaceAsync(CatalogCache.Namespace, CancellationToken.None);
        await app.Sender.Send(new GetProductsQuery());

        Assert.Equal(2, app.Products.Calls);
    }

    [Fact]
    public async Task Page_size_is_clamped_and_page_is_at_least_one()
    {
        var app = new TestApp(null, TestApp.Product("A"));

        var result = await app.Sender.Send(new GetProductsQuery(Page: -5, PageSize: 10_000));

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task Missing_product_returns_null_and_is_not_cached()
    {
        var app = new TestApp();
        var id = Guid.NewGuid();

        Assert.Null(await app.Sender.Send(new GetProductQuery(id)));
        Assert.Null(await app.Sender.Send(new GetProductQuery(id)));

        Assert.Equal(2, app.Products.Calls);
    }

    [Fact]
    public async Task Existing_product_is_mapped_to_dto()
    {
        var product = TestApp.Product("Keyboard");
        var app = new TestApp(null, product);

        var dto = await app.Sender.Send(new GetProductQuery(product.Id));

        Assert.NotNull(dto);
        Assert.Equal("Keyboard", dto.Name);
        Assert.Equal(10m, dto.Price);
        Assert.Equal("USD", dto.Currency);
    }
}

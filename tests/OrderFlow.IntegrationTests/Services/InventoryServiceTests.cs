using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.Inventory;
using OrderFlow.Inventory.Data;
using OrderFlow.Messaging;

namespace OrderFlow.IntegrationTests.Services;

[Collection(ServicesTestGroup.Name)]
public sealed class InventoryServiceTests(ServicesFixture fixture)
{
    private static readonly string ReservedKey = MessageCatalog.RoutingKeyOf<StockReserved>();
    private static readonly string FailedKey = MessageCatalog.RoutingKeyOf<StockReservationFailed>();

    private async Task<TestHost> StartInventoryAsync(Action<IServiceCollection>? extra = null)
    {
        // A previous test may have left unprocessed messages in the shared service queue.
        await fixture.PurgeQueueAsync(Topology.Inventory);
        var host = fixture.BuildHost(services =>
        {
            services.AddInventoryService(
                new ConfigurationBuilder().AddInMemoryCollection(fixture.Settings).Build(),
                options => options.RetryDelay = TimeSpan.FromMilliseconds(100));
            extra?.Invoke(services);
        });
        await host.StartAsync();
        return host;
    }

    private static async Task SeedStockAsync(TestHost host, Guid productId, int available)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        db.StockItems.Add(StockItem.Create(productId, available));
        await db.SaveChangesAsync();
    }

    private static async Task<int> StockOfAsync(TestHost host, Guid productId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.StockItems.AsNoTracking().Where(s => s.ProductId == productId).Select(s => s.Available).SingleAsync();
    }

    private async Task<string> OutcomeQueueAsync()
    {
        var queue = RabbitMqFixture.NewQueue(ReservedKey, FailedKey);
        await fixture.DeclareBoundQueueAsync(queue.Name, ReservedKey);
        await fixture.DeclareBoundQueueAsync(queue.Name, FailedKey);
        return queue.Name;
    }

    private static OrderCreated Order(Guid productId, int quantity, Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(), Guid.NewGuid(), [new OrderLine(productId, quantity)], 50m, "USD");

    [Fact]
    public async Task Reserves_stock_and_answers_with_StockReserved_keeping_the_correlation()
    {
        await using var inventory = await StartInventoryAsync();
        var product = Guid.NewGuid();
        await SeedStockAsync(inventory, product, 5);
        var outcomes = await OutcomeQueueAsync();
        var order = Order(product, 2);

        var sentId = await inventory.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order, MessageContext.ForNewFlow("flow-inv-1"));
        var reserved = (await fixture.ReceiveAsync<StockReserved>(outcomes, 1)).Single();

        Assert.Equal(order.OrderId, reserved.OrderId);
        Assert.Equal(order.CustomerId, reserved.CustomerId);
        Assert.Equal(50m, reserved.TotalAmount);
        Assert.Equal(3, await StockOfAsync(inventory, product));
        Assert.NotEqual(Guid.Empty, sentId);
        await inventory.StopAsync();
    }

    [Fact]
    public async Task Rejects_an_order_when_stock_is_insufficient_and_leaves_stock_untouched()
    {
        await using var inventory = await StartInventoryAsync();
        var product = Guid.NewGuid();
        await SeedStockAsync(inventory, product, 1);
        var outcomes = await OutcomeQueueAsync();
        var order = Order(product, 3);

        await inventory.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var failed = (await fixture.ReceiveAsync<StockReservationFailed>(outcomes, 1)).Single();

        Assert.Equal(order.OrderId, failed.OrderId);
        Assert.Contains("Not enough stock", failed.Reason, StringComparison.Ordinal);
        Assert.Equal(1, await StockOfAsync(inventory, product));
        await inventory.StopAsync();
    }

    [Fact]
    public async Task Rejects_an_order_for_an_unknown_product()
    {
        await using var inventory = await StartInventoryAsync();
        var outcomes = await OutcomeQueueAsync();
        var order = Order(Guid.NewGuid(), 1);

        await inventory.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var failed = (await fixture.ReceiveAsync<StockReservationFailed>(outcomes, 1)).Single();

        Assert.Equal(order.OrderId, failed.OrderId);
        Assert.Contains("Unknown product", failed.Reason, StringComparison.Ordinal);
        await inventory.StopAsync();
    }

    [Fact]
    public async Task A_duplicate_OrderCreated_reserves_once_and_repeats_the_same_answer()
    {
        await using var inventory = await StartInventoryAsync();
        var product = Guid.NewGuid();
        await SeedStockAsync(inventory, product, 5);
        var outcomes = await OutcomeQueueAsync();
        var order = Order(product, 2);
        var publisher = inventory.Services.GetRequiredService<IMessagePublisher>();

        await publisher.PublishAsync(order);
        await publisher.PublishAsync(order); // the broker delivers at-least-once; here the duplicate is explicit

        var answers = await fixture.ReceiveAsync<StockReserved>(outcomes, 2);

        Assert.Equal(2, answers.Count);
        Assert.All(answers, answer => Assert.Equal(order.OrderId, answer.OrderId));
        Assert.Equal(3, await StockOfAsync(inventory, product)); // reserved once, not twice
        await inventory.StopAsync();
    }

    [Fact]
    public async Task A_lost_answer_is_recovered_by_redelivery_without_reserving_twice()
    {
        // The reservation commits, then publishing the answer fails once (the "crash after commit" window).
        await using var directPublisher = fixture.BuildHost();
        var failures = 1;
        await using var inventory = await StartInventoryAsync(services => services.AddSingleton<IMessagePublisher>(
            new FailingOncePublisher(directPublisher.Services.GetRequiredService<IMessagePublisher>(), () => failures-- > 0)));
        var product = Guid.NewGuid();
        await SeedStockAsync(inventory, product, 5);
        var outcomes = await OutcomeQueueAsync();
        var order = Order(product, 2);

        await directPublisher.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var reserved = (await fixture.ReceiveAsync<StockReserved>(outcomes, 1, seconds: 30)).Single();

        Assert.Equal(order.OrderId, reserved.OrderId);
        Assert.Equal(3, await StockOfAsync(inventory, product));
        await inventory.StopAsync();
    }

    [Fact]
    public async Task Competing_consumers_never_oversell_and_every_order_gets_exactly_one_decision()
    {
        const int stock = 10;
        const int orders = 30;
        await using var first = await StartInventoryAsync();
        await using var second = await StartInventoryAsync(); // same queue: competing consumers (purge ran before the first start only)
        var product = Guid.NewGuid();
        await SeedStockAsync(first, product, stock);
        var outcomes = await OutcomeQueueAsync();
        var publisher = first.Services.GetRequiredService<IMessagePublisher>();

        var messages = Enumerable.Range(0, orders).Select(_ => Order(product, 1)).ToList();
        foreach (var message in messages)
        {
            await publisher.PublishAsync(message);
        }

        // Every decision (reserved or failed) is answered with one message; competing consumers may hit a version
        // conflict on the stock row, in which case the message is requeued and decided on the next attempt.
        var answers = await fixture.ReceiveAsync<StockReserved>(outcomes, orders, seconds: 60);
        await first.StopAsync();
        await second.StopAsync();

        Assert.Equal(orders, answers.Select(a => a.OrderId).Distinct().Count());
        await using var scope = first.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var orderIds = messages.Select(m => m.OrderId).ToList();
        var decisions = await db.Reservations.AsNoTracking().Where(r => orderIds.Contains(r.OrderId)).ToListAsync();
        Assert.Equal(orders, decisions.Count);
        Assert.Equal(stock, decisions.Count(r => r.Status == ReservationStatus.Reserved));
        Assert.Equal(orders - stock, decisions.Count(r => r.Status == ReservationStatus.Failed));
        Assert.Equal(0, await StockOfAsync(first, product));
    }

    private sealed class FailingOncePublisher(IMessagePublisher inner, Func<bool> shouldFail) : IMessagePublisher
    {
        public Task<Guid> PublishAsync<T>(T message, MessageContext? cause = null, CancellationToken cancellationToken = default)
            where T : class =>
            shouldFail()
                ? throw new MessagePublishException("simulated broker failure")
                : inner.PublishAsync(message, cause, cancellationToken);
    }
}

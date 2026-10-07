using System.Net;
using System.Net.Http.Json;
using OrderFlow.Application.Orders;
using OrderFlow.Contracts;

namespace OrderFlow.IntegrationTests.Platform;

/// <summary>The three services together, through a real broker and database. Each test is a story from the demo script.</summary>
[Collection(PlatformTestGroup.Name)]
public sealed class PlatformEndToEndTests(PlatformFixture platform)
{
    [Fact]
    public async Task An_order_travels_the_whole_chain_and_ends_confirmed()
    {
        var product = await platform.SeedProductAsync(stock: 5, price: 20m);
        var (client, _) = await platform.CreateCustomerAsync();

        var accepted = await PlatformFixture.PlaceOrderAsync(client, product, 2);
        Assert.Equal("Pending", accepted.Status); // the HTTP call returns before the other services have worked

        var confirmed = await PlatformFixture.WaitForOrderAsync(client, accepted.Id, o => o.Status == "Confirmed", "the order to be confirmed");

        Assert.Equal(40m, confirmed.Total);
        Assert.Null(confirmed.CancellationReason);
        Assert.Equal(3, await platform.StockOfAsync(product));
    }

    [Fact]
    public async Task A_bank_decline_cancels_the_order_with_the_reason()
    {
        var product = await platform.SeedProductAsync(stock: 5);
        var (client, _) = await platform.CreateCustomerAsync();
        await platform.SetPaymentModeAsync(PaymentEmulator.Decline);
        try
        {
            var accepted = await PlatformFixture.PlaceOrderAsync(client, product, 1);

            var cancelled = await PlatformFixture.WaitForOrderAsync(client, accepted.Id, PlatformFixture.IsFinal, "the order to be cancelled");

            Assert.Equal("Cancelled", cancelled.Status);
            Assert.Contains("declined", cancelled.CancellationReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await platform.SetPaymentModeAsync(null);
        }
    }

    [Fact]
    public async Task A_payment_timeout_cancels_the_order()
    {
        var product = await platform.SeedProductAsync(stock: 5);
        var (client, _) = await platform.CreateCustomerAsync();
        await platform.SetPaymentModeAsync(PaymentEmulator.Timeout);
        try
        {
            var accepted = await PlatformFixture.PlaceOrderAsync(client, product, 1);

            var cancelled = await PlatformFixture.WaitForOrderAsync(client, accepted.Id, PlatformFixture.IsFinal, "the timed-out payment", seconds: 40);

            Assert.Equal("Cancelled", cancelled.Status);
            Assert.Contains("timed out", cancelled.CancellationReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await platform.SetPaymentModeAsync(null);
        }
    }

    [Fact]
    public async Task Ordering_more_than_is_in_stock_cancels_the_order_and_keeps_the_stock()
    {
        var product = await platform.SeedProductAsync(stock: 1);
        var (client, _) = await platform.CreateCustomerAsync();

        var accepted = await PlatformFixture.PlaceOrderAsync(client, product, 3);
        var cancelled = await PlatformFixture.WaitForOrderAsync(client, accepted.Id, PlatformFixture.IsFinal, "the order to be refused");

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Contains("Not enough stock", cancelled.CancellationReason, StringComparison.Ordinal);
        Assert.Equal(1, await platform.StockOfAsync(product));
    }

    [Fact]
    public async Task While_Payments_is_stopped_orders_wait_and_complete_after_it_starts()
    {
        var product = await platform.SeedProductAsync(stock: 10);
        var customers = new List<HttpClient>();
        for (var i = 0; i < 3; i++)
        {
            customers.Add((await platform.CreateCustomerAsync()).Client);
        }

        await platform.StopPaymentsAsync();
        try
        {
            var orders = new List<(HttpClient Client, OrderDto Order)>();
            foreach (var client in customers)
            {
                orders.Add((client, await PlatformFixture.PlaceOrderAsync(client, product, 1)));
            }

            // Stock is reserved, but nobody is charging: the orders wait and the messages queue up in the broker.
            foreach (var (client, order) in orders)
            {
                await PlatformFixture.WaitForOrderAsync(client, order.Id, o => o.Status == "StockReserved", "stock to be reserved while Payments is down");
            }

            Assert.Equal(3u, await platform.ReadyMessagesAsync(Topology.Payments.Name));

            await platform.StartPaymentsAsync();

            foreach (var (client, order) in orders)
            {
                var done = await PlatformFixture.WaitForOrderAsync(client, order.Id, PlatformFixture.IsFinal, "the queued payment to be processed");
                Assert.Equal("Confirmed", done.Status);
            }

            await RabbitMqFixtureEventually(() => platform.ReadyMessagesAsync(Topology.Payments.Name), 0u);
        }
        finally
        {
            if (platform.Payments is null)
            {
                await platform.StartPaymentsAsync();
            }
        }
    }

    [Fact]
    public async Task Buyers_competing_for_scarce_stock_never_oversell()
    {
        const int stock = 5;
        const int buyers = 12;
        var product = await platform.SeedProductAsync(stock);

        var customers = new List<HttpClient>();
        for (var i = 0; i < buyers; i++)
        {
            var (client, _) = await platform.CreateCustomerAsync();
            (await client.PostAsJsonAsync("/api/cart/items", new { productId = product, quantity = 1 })).EnsureSuccessStatusCode();
            customers.Add(client);
        }

        var accepted = await Task.WhenAll(customers.Select(async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
        }));

        var finals = new List<OrderDto>();
        for (var i = 0; i < buyers; i++)
        {
            finals.Add(await PlatformFixture.WaitForOrderAsync(customers[i], accepted[i].Id, PlatformFixture.IsFinal, $"order {i} to be decided", seconds: 60));
        }

        Assert.Equal(stock, finals.Count(o => o.Status == "Confirmed"));
        Assert.Equal(buyers - stock, finals.Count(o => o.Status == "Cancelled"));
        Assert.Equal(0, await platform.StockOfAsync(product));
    }

    [Fact]
    public async Task Duplicate_checkout_requests_create_one_order_and_reserve_stock_once()
    {
        var product = await platform.SeedProductAsync(stock: 10);
        var (client, _) = await platform.CreateCustomerAsync();
        (await client.PostAsJsonAsync("/api/cart/items", new { productId = product, quantity = 3 })).EnsureSuccessStatusCode();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }));

        var ids = new HashSet<Guid>();
        foreach (var response in responses)
        {
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"Unexpected {response.StatusCode}");
            ids.Add((await response.Content.ReadFromJsonAsync<OrderDto>())!.Id);
        }

        var orderId = Assert.Single(ids);
        var confirmed = await PlatformFixture.WaitForOrderAsync(client, orderId, PlatformFixture.IsFinal, "the single order to finish");
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal(7, await platform.StockOfAsync(product)); // 10 - 3, reserved once
    }

    private static async Task RabbitMqFixtureEventually(Func<Task<uint>> read, uint expected) =>
        await Messaging.RabbitMqFixture.EventuallyAsync(async () => await read() == expected, $"the queue to reach {expected} message(s)");
}

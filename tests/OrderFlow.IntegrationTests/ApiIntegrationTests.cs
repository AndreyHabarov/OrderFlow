using System.Net;
using System.Net.Http.Json;
using OrderFlow.Application.Auth;
using OrderFlow.Application.Carts;
using OrderFlow.Application.Catalog;
using OrderFlow.Application.Orders;

namespace OrderFlow.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class ApiIntegrationTests(ApiFactory factory)
{
    private static async Task AddToCartAsync(HttpClient client, Guid productId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/cart/items", new { productId, quantity });
        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> CheckoutAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Readiness_probe_sees_all_dependencies()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_valid_token()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/cart")).StatusCode);

        var (client, _) = await factory.CreateCustomerAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cart")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cart")).StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_rotated_and_reuse_revokes_the_session()
    {
        var (client, auth) = await factory.CreateCustomerAsync();

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var afterReuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_is_a_conflict()
    {
        var client = factory.CreateClient();
        var body = new { email = $"{Guid.NewGuid():N}@example.com", password = "correct-horse-battery" };

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", body)).StatusCode);
    }

    [Fact]
    public async Task Checkout_accepts_the_order_as_pending_and_empties_the_cart()
    {
        // Pending is the answer of this service alone; with Inventory and Payments running the order moves on
        // (covered end to end in PlatformEndToEndTests).
        var productId = await factory.CreateProductAsync(price: 25m);
        var (client, _) = await factory.CreateCustomerAsync();
        await AddToCartAsync(client, productId, 3);

        var response = await CheckoutAsync(client, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal("Pending", order.Status);
        Assert.Equal(75m, order.Total);

        var cart = await client.GetFromJsonAsync<CartDto>("/api/cart");
        Assert.Empty(cart!.Items);
    }

    [Fact]
    public async Task Concurrent_cart_updates_by_a_new_customer_neither_fail_nor_lose_items()
    {
        // Found in the browser: two quick clicks by a customer without a cart raced to create it and one request got a 500.
        var first = await factory.CreateProductAsync();
        var second = await factory.CreateProductAsync();
        var (client, _) = await factory.CreateCustomerAsync();

        var adds = Enumerable.Range(0, 4).SelectMany(_ => new[] { first, second })
            .Select(id => client.PostAsJsonAsync("/api/cart/items", new { productId = id, quantity = 1 }));
        var responses = await Task.WhenAll(adds);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var cart = (await client.GetFromJsonAsync<CartDto>("/api/cart"))!;
        Assert.Equal(2, cart.Items.Count);
        Assert.All(cart.Items, item => Assert.Equal(4, item.Quantity));
    }

    [Fact]
    public async Task Customers_cannot_read_each_others_orders()
    {
        var productId = await factory.CreateProductAsync();
        var (owner, _) = await factory.CreateCustomerAsync();
        await AddToCartAsync(owner, productId, 1);
        var order = (await (await CheckoutAsync(owner, Guid.NewGuid().ToString())).Content.ReadFromJsonAsync<OrderDto>())!;

        var (stranger, _) = await factory.CreateCustomerAsync();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task Parallel_checkouts_with_the_same_idempotency_key_create_exactly_one_order()
    {
        // Timing decides which race window is hit (before the cart is emptied, during the save, or right after),
        // so the burst is repeated with fresh customers; a single run can pass by luck (it did, locally, before CI caught it).
        for (var round = 0; round < 10; round++)
        {
            var productId = await factory.CreateProductAsync();
            var (client, _) = await factory.CreateCustomerAsync();
            await AddToCartAsync(client, productId, 3);
            var key = Guid.NewGuid().ToString();

            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CheckoutAsync(client, key)));

            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"Round {round}: unexpected {r.StatusCode}"));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            var orderIds = new HashSet<Guid>();
            foreach (var response in responses)
            {
                orderIds.Add((await response.Content.ReadFromJsonAsync<OrderDto>())!.Id);
            }

            Assert.Single(orderIds);
            var orders = await client.GetFromJsonAsync<List<OrderDto>>("/api/orders");
            Assert.Single(orders!);
        }
    }
}

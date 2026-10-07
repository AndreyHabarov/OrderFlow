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
    public async Task Checkout_creates_an_order_reduces_stock_and_refreshes_the_cached_catalog()
    {
        var productId = await factory.CreateProductAsync(stock: 10, price: 25m);
        var (client, _) = await factory.CreateCustomerAsync();

        var before = await client.GetFromJsonAsync<ProductDto>($"/api/products/{productId}");
        Assert.Equal(10, before!.StockQuantity);

        await AddToCartAsync(client, productId, 3);
        var response = await CheckoutAsync(client, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>())!;
        Assert.Equal("StockReserved", order.Status);
        Assert.Equal(75m, order.Total);

        Assert.Equal(7, await factory.GetStockAsync(productId));

        // The cache was invalidated by the checkout, so the API shows the new stock.
        var after = await client.GetFromJsonAsync<ProductDto>($"/api/products/{productId}");
        Assert.Equal(7, after!.StockQuantity);

        var cart = await client.GetFromJsonAsync<CartDto>("/api/cart");
        Assert.Empty(cart!.Items);
    }

    [Fact]
    public async Task Customers_cannot_read_each_others_orders()
    {
        var productId = await factory.CreateProductAsync(stock: 5);
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
        var productId = await factory.CreateProductAsync(stock: 10);
        var (client, _) = await factory.CreateCustomerAsync();
        await AddToCartAsync(client, productId, 3);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CheckoutAsync(client, key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"Unexpected {r.StatusCode}"));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var orderIds = new HashSet<Guid>();
        foreach (var response in responses)
        {
            orderIds.Add((await response.Content.ReadFromJsonAsync<OrderDto>())!.Id);
        }

        Assert.Single(orderIds);
        Assert.Equal(7, await factory.GetStockAsync(productId));
        var orders = await client.GetFromJsonAsync<List<OrderDto>>("/api/orders");
        Assert.Single(orders!);
    }

    [Fact]
    public async Task Concurrent_buyers_of_scarce_stock_never_oversell()
    {
        const int stock = 5;
        const int buyers = 12;
        var productId = await factory.CreateProductAsync(stock);

        var customers = new List<HttpClient>();
        for (var i = 0; i < buyers; i++)
        {
            var (client, _) = await factory.CreateCustomerAsync();
            await AddToCartAsync(client, productId, 1);
            customers.Add(client);
        }

        var responses = await Task.WhenAll(customers.Select(c => CheckoutAsync(c, Guid.NewGuid().ToString())));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Count(r => r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);

        // 422 = not enough stock, 409 = lost an optimistic concurrency race (safe to retry). Anything else is a bug.
        Assert.Equal(buyers, created + rejected);
        Assert.InRange(created, 1, stock);
        Assert.Equal(stock - created, await factory.GetStockAsync(productId));
    }
}

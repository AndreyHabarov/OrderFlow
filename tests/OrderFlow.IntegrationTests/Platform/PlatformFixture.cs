using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Auth;
using OrderFlow.Application.Orders;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.IntegrationTests.Services;
using OrderFlow.Inventory;
using OrderFlow.Inventory.Data;
using OrderFlow.Payments;

namespace OrderFlow.IntegrationTests.Platform;

/// <summary>The Orders API running against the shared containers (a different composition of the same pieces).</summary>
public sealed class PlatformApiFactory(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.UseSetting("Jwt:SigningKey", "platform-tests-signing-key-with-more-than-32-chars");
        builder.UseSetting("Seq:Url", "http://127.0.0.1:1");
    }
}

/// <summary>
/// The whole platform in one process: the Orders API, the Inventory service and the Payments service, all talking
/// through a real RabbitMQ and PostgreSQL, exactly like in docker compose. Inventory and Payments can be stopped and
/// started by a test.
/// </summary>
#pragma warning disable CA1001 // the API factory is disposed in DisposeAsync (IAsyncLifetime), which xUnit calls
public sealed class PlatformFixture : ServicesFixture
{
    private PlatformApiFactory? _api;

    public TestHost? Inventory { get; private set; }

    public TestHost? Payments { get; private set; }

    public PlatformApiFactory Api => _api!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = new PlatformApiFactory(Settings);
        _ = _api.Services; // starts the API host: migrations, topology, the order consumer
        await StartInventoryAsync();
        await StartPaymentsAsync();
    }

    public override async Task DisposeAsync()
    {
        if (Payments is not null)
        {
            await Payments.StopAsync();
            await Payments.DisposeAsync();
        }

        if (Inventory is not null)
        {
            await Inventory.StopAsync();
            await Inventory.DisposeAsync();
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    public async Task StartInventoryAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
        Inventory = BuildHost(services => services.AddInventoryService(configuration, options => options.RetryDelay = TimeSpan.FromMilliseconds(100)));
        await Inventory.StartAsync();
    }

    public async Task StartPaymentsAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
        Payments = BuildHost(services => services.AddPaymentsService(configuration, options => options.RetryDelay = TimeSpan.FromMilliseconds(100)));
        await Payments.StartAsync();
    }

    public async Task StopPaymentsAsync()
    {
        if (Payments is null)
        {
            return;
        }

        await Payments.StopAsync();
        await Payments.DisposeAsync();
        Payments = null;
    }

    public async Task<(HttpClient Client, AuthResponse Auth)> CreateCustomerAsync()
    {
        var client = Api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = $"{Guid.NewGuid():N}@example.com", password = "correct-horse-battery" });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    /// <summary>A product in the Orders catalog and the same product with <paramref name="stock"/> units in Inventory.</summary>
    public async Task<Guid> SeedProductAsync(int stock, decimal price = 20m)
    {
        var product = Product.Create($"E2E product {Guid.NewGuid():N}", "end to end", new Money(price));

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            orders.Products.Add(product);
            await orders.SaveChangesAsync();
        }

        await using (var scope = Inventory!.Services.CreateAsyncScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            inventory.StockItems.Add(StockItem.Create(product.Id, stock));
            await inventory.SaveChangesAsync();
        }

        return product.Id;
    }

    public async Task<int> StockOfAsync(Guid productId)
    {
        await using var scope = Inventory!.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await inventory.StockItems.AsNoTracking().Where(s => s.ProductId == productId).Select(s => s.Available).SingleAsync();
    }

    public static async Task<OrderDto> PlaceOrderAsync(HttpClient client, Guid productId, int quantity, string? idempotencyKey = null)
    {
        (await client.PostAsJsonAsync("/api/cart/items", new { productId, quantity })).EnsureSuccessStatusCode();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    /// <summary>Polls the order until <paramref name="done"/> holds, failing with the last seen state when time runs out.</summary>
    public static async Task<OrderDto> WaitForOrderAsync(HttpClient client, Guid orderId, Func<OrderDto, bool> done, string because, int seconds = 30)
    {
        OrderDto? last = null;
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            last = await client.GetFromJsonAsync<OrderDto>($"/api/orders/{orderId}");
            if (last is not null && done(last))
            {
                return last;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Timed out waiting for {because}. Last status: {last?.Status} ({last?.CancellationReason})");
        return last!;
    }

    public static bool IsFinal(OrderDto order) => order.Status is "Confirmed" or "Cancelled";
}

#pragma warning restore CA1001

[CollectionDefinition(Name)]
public sealed class PlatformTestGroup : ICollectionFixture<PlatformFixture>
{
    public const string Name = "platform";
}

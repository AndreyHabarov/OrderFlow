using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Auth;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;
using OrderFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace OrderFlow.IntegrationTests;

/// <summary>
/// Starts the real API against real PostgreSQL, Redis and RabbitMQ containers (same image versions as docker-compose).
/// One instance is shared by all tests; tests isolate themselves by creating their own users and products.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.2-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3.6-alpine").Build();

    public async Task InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("RabbitMq:Host", _rabbit.Hostname);
        builder.UseSetting("RabbitMq:Port", _rabbit.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RabbitMq:User", "rabbitmq");
        builder.UseSetting("RabbitMq:Password", "rabbitmq");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-with-more-than-32-chars");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seq:Url", "http://127.0.0.1:1");
    }

    /// <summary>Registers a new customer and returns a client that sends its access token.</summary>
    public async Task<(HttpClient Client, AuthResponse Auth)> CreateCustomerAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = $"{Guid.NewGuid():N}@example.com", password = "correct-horse-battery" });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    public async Task<Guid> CreateProductAsync(int stock, decimal price = 10m)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = Product.Create($"Test product {Guid.NewGuid():N}", "integration test", new Money(price), stock);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    public async Task<int> GetStockAsync(Guid productId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

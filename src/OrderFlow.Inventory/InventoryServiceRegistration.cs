using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Demo;
using OrderFlow.Inventory.Data;
using OrderFlow.Inventory.Handlers;
using OrderFlow.Messaging;

namespace OrderFlow.Inventory;

public static class InventoryServiceRegistration
{
    /// <summary>Everything the Inventory service needs. The same call is used by the worker host and by the tests.</summary>
    public static IServiceCollection AddInventoryService(this IServiceCollection services, IConfiguration configuration, Action<ConsumerOptions>? consumerOptions = null)
    {
        var connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

        services.AddRabbitMq(configuration);
        services.AddDbContext<InventoryDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        // Registered before the consumer, so the schema and the demo stock exist before the first message is handled.
        services.AddHostedService<InventoryInitializer>();
        services.AddMessageConsumer(Topology.Inventory, builder => builder.Handle<OrderCreated, OrderCreatedHandler>(), consumerOptions);
        return services;
    }
}

/// <summary>Applies migrations and, for local development, inserts stock for the demo products.</summary>
internal sealed class InventoryInitializer(IServiceProvider services, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (configuration.GetValue<bool>("Inventory:SeedDemoStock") && !await db.StockItems.AnyAsync(cancellationToken))
        {
            db.StockItems.AddRange(DemoCatalog.Products.Select(product => StockItem.Create(product.Id, product.Stock)));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

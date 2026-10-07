using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.Messaging;
using OrderFlow.Payments.Data;
using OrderFlow.Payments.Handlers;
using StackExchange.Redis;

namespace OrderFlow.Payments;

public static class PaymentsServiceRegistration
{
    /// <summary>Everything the Payments service needs. The same call is used by the worker host and by the tests.</summary>
    public static IServiceCollection AddPaymentsService(this IServiceCollection services, IConfiguration configuration, Action<ConsumerOptions>? consumerOptions = null)
    {
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.AddRabbitMq(configuration);
        services.AddDbContext<PaymentsDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Postgres") ?? string.Empty, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        // AbortOnConnectFail=false: the service starts and keeps working (with the default mode) while Redis is down.
        var redisOptions = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<IPaymentModeProvider, RedisPaymentModeProvider>();

        services.AddHostedService<PaymentsInitializer>();
        services.AddMessageConsumer(Topology.Payments, builder => builder.Handle<StockReserved, StockReservedHandler>(), consumerOptions);
        return services;
    }
}

/// <summary>Applies migrations before the first message is handled.</summary>
internal sealed class PaymentsInitializer(IServiceProvider services, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

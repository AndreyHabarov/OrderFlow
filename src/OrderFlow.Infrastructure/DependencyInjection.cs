using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace OrderFlow.Infrastructure;

public static class DependencyInjection
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres") ?? string.Empty;
        var redis = configuration.GetConnectionString("Redis") ?? string.Empty;
        var rabbit = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"] ?? "localhost",
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = configuration["RabbitMq:User"] ?? string.Empty,
            Password = configuration["RabbitMq:Password"] ?? string.Empty
        };

        services.AddSingleton(rabbit);

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddHealthChecks()
            .AddNpgSql(postgres, name: "postgres", tags: [ReadyTag])
            .AddRedis(redis, name: "redis", tags: [ReadyTag])
            .AddRabbitMQ(sp => sp.GetRequiredService<ConnectionFactory>().CreateConnectionAsync(), name: "rabbitmq", tags: [ReadyTag]);

        return services;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}

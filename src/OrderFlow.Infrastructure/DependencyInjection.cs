using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddHealthChecks()
            .AddNpgSql(postgres, name: "postgres", tags: [ReadyTag])
            .AddRedis(redis, name: "redis", tags: [ReadyTag])
            .AddRabbitMQ(sp => sp.GetRequiredService<ConnectionFactory>().CreateConnectionAsync(), name: "rabbitmq", tags: [ReadyTag]);

        return services;
    }
}

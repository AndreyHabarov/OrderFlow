using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Auth;
using OrderFlow.Application.Common;
using OrderFlow.Infrastructure.Auth;
using OrderFlow.Infrastructure.Caching;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Persistence.Repositories;
using RabbitMQ.Client;
using StackExchange.Redis;

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

        // AbortOnConnectFail=false: the API starts even if Redis is down; the multiplexer reconnects in the background.
        var redisOptions = ConfigurationOptions.Parse(redis);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<ICacheService, RedisCacheService>();

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters. Set it with user-secrets or the JWT_SIGNING_KEY environment variable.")
            .ValidateOnStart();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

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

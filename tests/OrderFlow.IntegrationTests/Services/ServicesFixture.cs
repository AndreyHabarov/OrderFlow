using OrderFlow.Contracts;
using OrderFlow.IntegrationTests.Messaging;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace OrderFlow.IntegrationTests.Services;

/// <summary>RabbitMQ, PostgreSQL and Redis for tests that run service hosts in-process.</summary>
public sealed class ServicesFixture : RabbitMqFixture
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.10.2-alpine").Build();

    public override async Task InitializeAsync() =>
        await Task.WhenAll(base.InitializeAsync(), _postgres.StartAsync(), _redis.StartAsync());

    public override async Task DisposeAsync() =>
        await Task.WhenAll(base.DisposeAsync(), _postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());

    public override Dictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["ConnectionStrings:Postgres"] = _postgres.GetConnectionString();
            settings["ConnectionStrings:Redis"] = _redis.GetConnectionString();
            settings["Database:MigrateOnStartup"] = "true";
            return settings;
        }
    }

    /// <summary>Sets (or, with null, clears) the payment emulator mode the same way the admin UI will.</summary>
    public async Task SetPaymentModeAsync(string? mode)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        var database = redis.GetDatabase();
        if (mode is null)
        {
            await database.KeyDeleteAsync(PaymentEmulator.ModeKey);
        }
        else
        {
            await database.StringSetAsync(PaymentEmulator.ModeKey, mode);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ServicesTestGroup : ICollectionFixture<ServicesFixture>
{
    public const string Name = "services";
}

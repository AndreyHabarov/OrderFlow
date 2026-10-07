using OrderFlow.IntegrationTests.Messaging;
using Testcontainers.PostgreSql;

namespace OrderFlow.IntegrationTests.Services;

/// <summary>RabbitMQ plus PostgreSQL for tests that run service hosts in-process.</summary>
public sealed class ServicesFixture : RabbitMqFixture
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public override async Task InitializeAsync() => await Task.WhenAll(base.InitializeAsync(), _postgres.StartAsync());

    public override async Task DisposeAsync() => await Task.WhenAll(base.DisposeAsync(), _postgres.DisposeAsync().AsTask());

    public override Dictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["ConnectionStrings:Postgres"] = _postgres.GetConnectionString();
            settings["Database:MigrateOnStartup"] = "true";
            return settings;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ServicesTestGroup : ICollectionFixture<ServicesFixture>
{
    public const string Name = "services";
}

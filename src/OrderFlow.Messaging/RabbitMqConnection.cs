using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Messaging;

/// <summary>
/// One shared, automatically recovering connection per process (connections are expensive, channels are cheap).
/// The first caller opens it; the broker may still be starting, so opening is retried.
/// </summary>
internal sealed partial class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private const int MaxAttempts = 10;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.User,
                Password = settings.Password,
                ClientProvidedName = settings.ClientName,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync(cancellationToken);
                    return _connection;
                }
                catch (BrokerUnreachableException ex) when (attempt < MaxAttempts)
                {
                    LogConnectFailed(ex, attempt, MaxAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Declares the shared durable topic exchange. Declaring is idempotent, so every service does it.</summary>
    public static Task DeclareExchangeAsync(IChannel channel, CancellationToken cancellationToken) =>
        channel.ExchangeDeclareAsync(Topology.EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ is not reachable yet (attempt {Attempt} of {Max}); retrying")]
    private partial void LogConnectFailed(Exception exception, int attempt, int max);
}

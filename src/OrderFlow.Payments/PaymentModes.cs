using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;
using StackExchange.Redis;

namespace OrderFlow.Payments;

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>Behaviour when Redis has no value or cannot be reached.</summary>
    public string DefaultMode { get; set; } = PaymentEmulator.Success;

    /// <summary>How long the emulated bank "thinks" before a timeout is reported.</summary>
    public TimeSpan TimeoutDelay { get; set; } = TimeSpan.FromSeconds(3);
}

public enum PaymentMode
{
    Success,
    Decline,
    Timeout
}

internal interface IPaymentModeProvider
{
    Task<PaymentMode> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the emulator mode from Redis on every payment, so it can be changed while the service runs.
/// The mode is a convenience for demos, so a Redis outage must not stop payments: the configured default applies.
/// </summary>
internal sealed partial class RedisPaymentModeProvider(
    IConnectionMultiplexer redis,
    IOptions<PaymentOptions> options,
    ILogger<RedisPaymentModeProvider> logger) : IPaymentModeProvider
{
    public async Task<PaymentMode> GetAsync(CancellationToken cancellationToken)
    {
        var fallback = Parse(options.Value.DefaultMode) ?? PaymentMode.Success;
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(PaymentEmulator.ModeKey).WaitAsync(cancellationToken);
            if (!value.HasValue)
            {
                return fallback;
            }

            var mode = Parse(value.ToString());
            if (mode is null)
            {
                LogUnknownMode(value.ToString(), fallback);
                return fallback;
            }

            return mode.Value;
        }
        catch (RedisException ex)
        {
            LogRedisUnavailable(ex, fallback);
            return fallback;
        }
    }

    private static PaymentMode? Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        PaymentEmulator.Success => PaymentMode.Success,
        PaymentEmulator.Decline => PaymentMode.Decline,
        PaymentEmulator.Timeout => PaymentMode.Timeout,
        _ => null
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown payment mode '{Value}' in Redis; using {Fallback}")]
    private partial void LogUnknownMode(string value, PaymentMode fallback);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis is unavailable; using the default payment mode {Fallback}")]
    private partial void LogRedisUnavailable(Exception exception, PaymentMode fallback);
}

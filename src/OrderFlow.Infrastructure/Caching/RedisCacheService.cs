using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Abstractions;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure.Caching;

/// <summary>
/// Cache-aside on Redis. Keys are stored as <c>cache:{namespace}:{version}:{key}</c>.
/// Invalidation increments the namespace version (O(1)); stale keys are never read again and expire by TTL.
/// </summary>
internal sealed partial class RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetOrCreateAsync<T>(
        string cacheNamespace,
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
        where T : class
    {
        string? redisKey = null;
        try
        {
            var db = redis.GetDatabase();
            var version = await db.StringGetAsync(VersionKey(cacheNamespace));
            redisKey = $"cache:{cacheNamespace}:{(version.HasValue ? version.ToString() : "0")}:{key}";

            var cached = await db.StringGetAsync(redisKey);
            if (cached.HasValue)
            {
                var value = JsonSerializer.Deserialize<T>(cached.ToString(), JsonOptions);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            LogReadFailed(ex, cacheNamespace, key);
        }

        var created = await factory(cancellationToken);

        if (created is not null && redisKey is not null)
        {
            try
            {
                await redis.GetDatabase().StringSetAsync(redisKey, JsonSerializer.Serialize(created, JsonOptions), timeToLive);
            }
            catch (RedisException ex)
            {
                LogWriteFailed(ex, cacheNamespace, key);
            }
        }

        return created;
    }

    public async Task InvalidateNamespaceAsync(string cacheNamespace, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().StringIncrementAsync(VersionKey(cacheNamespace));
        }
        catch (RedisException ex)
        {
            LogInvalidateFailed(ex, cacheNamespace);
        }
    }

    private static string VersionKey(string cacheNamespace) => $"cache:version:{cacheNamespace}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache read failed for {CacheNamespace}:{Key}; falling back to the source")]
    private partial void LogReadFailed(Exception exception, string cacheNamespace, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache write failed for {CacheNamespace}:{Key}")]
    private partial void LogWriteFailed(Exception exception, string cacheNamespace, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache invalidation failed for {CacheNamespace}")]
    private partial void LogInvalidateFailed(Exception exception, string cacheNamespace);
}

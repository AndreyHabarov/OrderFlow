namespace OrderFlow.Application.Abstractions;

/// <summary>
/// Cache-aside helper. Keys live in a namespace; invalidating the namespace makes every key in it unreachable at once.
/// A cache failure must never fail the request: implementations fall back to the factory.
/// </summary>
public interface ICacheService
{
    Task<T?> GetOrCreateAsync<T>(
        string cacheNamespace,
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
        where T : class;

    Task InvalidateNamespaceAsync(string cacheNamespace, CancellationToken cancellationToken);
}

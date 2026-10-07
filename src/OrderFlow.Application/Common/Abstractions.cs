namespace OrderFlow.Application.Common;

/// <summary>The authenticated customer of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Throws <see cref="UnauthorizedException"/> when the request has no identity.</summary>
    Guid CustomerId { get; }
}

/// <summary>Commits all tracked changes in one database transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>True when the exception is a unique constraint violation (a concurrent duplicate insert).</summary>
    bool IsUniqueViolation(Exception exception);

    /// <summary>True when the exception means another request changed the same rows first (optimistic concurrency).</summary>
    bool IsConcurrencyConflict(Exception exception);

    /// <summary>Forgets every tracked entity so a retry starts from fresh database state.</summary>
    void DiscardChanges();
}

/// <summary>
/// Repeats a read-modify-write operation when it loses a race (unique violation or concurrency conflict).
/// Each attempt reloads fresh state, so the retry sees what the winner committed.
/// </summary>
internal static class ConflictRetry
{
    private const int MaxAttempts = 10;

    public static async Task<T> RunAsync<T>(IUnitOfWork unitOfWork, Func<Task<T>> attempt)
    {
        for (var number = 1; ; number++)
        {
            try
            {
                return await attempt();
            }
            catch (Exception ex) when (number < MaxAttempts && (unitOfWork.IsUniqueViolation(ex) || unitOfWork.IsConcurrencyConflict(ex)))
            {
                unitOfWork.DiscardChanges();
                await Task.Delay(TimeSpan.FromMilliseconds(10 * number));
            }
        }
    }
}

/// <summary>The requested resource does not exist (or is not visible to the caller). Mapped to 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public NotFoundException()
    {
    }
}

/// <summary>The request has no valid identity. Mapped to 401.</summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }

    public UnauthorizedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public UnauthorizedException()
    {
    }
}

/// <summary>The request conflicts with the current state (for example a duplicate email). Mapped to 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ConflictException()
    {
    }
}

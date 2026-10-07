using OrderFlow.Domain.Users;

namespace OrderFlow.Application.Auth;

public interface IUserRepository
{
    /// <param name="normalizedEmail">Already passed through <see cref="User.NormalizeEmail"/>.</param>
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(User user);
}

public interface IRefreshTokenRepository
{
    /// <summary>Tracked token (so it can be revoked in the current unit of work), or null.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(RefreshToken token);

    /// <summary>Revokes every active token of the user (used when reuse of a revoked token is detected).</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IPasswordService
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);

    /// <summary>Spends roughly the same time as a real verification, so unknown emails are not distinguishable by timing.</summary>
    void SimulateVerification();
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record NewRefreshToken(string Plain, string Hash);

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user, DateTimeOffset now);

    NewRefreshToken CreateRefreshToken();

    string HashRefreshToken(string plainToken);
}

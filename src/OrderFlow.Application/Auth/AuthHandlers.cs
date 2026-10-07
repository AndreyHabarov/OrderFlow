using FluentValidation;
using MediatR;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Users;

namespace OrderFlow.Application.Auth;

public sealed record UserDto(Guid Id, string Email, string Role);

public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, UserDto User);

public sealed record RegisterCommand(string Email, string Password) : IRequest<AuthResponse>;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponse>;

public sealed record LogoutCommand(string RefreshToken) : IRequest;

internal static class AuthRules
{
    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(200).EmailAddress();

    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(8).MaximumLength(100);
}

internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email).ValidEmail();
        RuleFor(c => c.Password).ValidPassword();
    }
}

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(c => c.RefreshToken).NotEmpty();
}

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(c => c.RefreshToken).NotEmpty();
}

/// <summary>Shared token issuing so register, login and refresh behave identically.</summary>
internal sealed class TokenIssuer(ITokenService tokens, IRefreshTokenRepository refreshTokens)
{
    public (AuthResponse Response, RefreshToken Stored) Issue(User user, DateTimeOffset now)
    {
        var access = tokens.CreateAccessToken(user, now);
        var refresh = tokens.CreateRefreshToken();
        var stored = RefreshToken.Create(user.Id, refresh.Hash, now, tokens.RefreshTokenLifetime);
        refreshTokens.Add(stored);

        var response = new AuthResponse(access.Value, access.ExpiresAt, refresh.Plain, new UserDto(user.Id, user.Email, user.Role.ToString()));
        return (response, stored);
    }
}

internal sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordService passwords,
    IUnitOfWork unitOfWork,
    TokenIssuer issuer,
    TimeProvider timeProvider) : IRequestHandler<RegisterCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await users.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new ConflictException("Email is already registered.");
        }

        var now = timeProvider.GetUtcNow();
        var user = User.Create(email, passwords.Hash(request.Password), UserRole.Customer, now);
        users.Add(user);
        var (response, _) = issuer.Issue(user, now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (unitOfWork.IsUniqueViolation(ex))
        {
            throw new ConflictException("Email is already registered.", ex);
        }

        return response;
    }
}

internal sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordService passwords,
    IUnitOfWork unitOfWork,
    TokenIssuer issuer,
    TimeProvider timeProvider) : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);
        if (user is null)
        {
            passwords.SimulateVerification();
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (!passwords.Verify(user.PasswordHash, request.Password))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        var (response, _) = issuer.Issue(user, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}

internal sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    ITokenService tokens,
    IUnitOfWork unitOfWork,
    TokenIssuer issuer,
    TimeProvider timeProvider) : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var stored = await refreshTokens.GetByHashAsync(tokens.HashRefreshToken(request.RefreshToken), cancellationToken)
                     ?? throw new UnauthorizedException("Invalid refresh token.");

        if (stored.IsRevoked)
        {
            // A rotated token is being used again: it was probably stolen. Kill every session of this user.
            await refreshTokens.RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("Refresh token reuse detected. Please sign in again.");
        }

        if (stored.IsExpired(now))
        {
            throw new UnauthorizedException("Refresh token expired.");
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
                   ?? throw new UnauthorizedException("Invalid refresh token.");

        var (response, replacement) = issuer.Issue(user, now);
        stored.Revoke(now, replacement.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}

internal sealed class LogoutCommandHandler(IRefreshTokenRepository refreshTokens, ITokenService tokens, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Idempotent and silent: an unknown token is not an error and tells the caller nothing.
        var stored = await refreshTokens.GetByHashAsync(tokens.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (stored is { IsRevoked: false })
        {
            stored.Revoke(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

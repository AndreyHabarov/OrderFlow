using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OrderFlow.Application.Auth;
using OrderFlow.Domain.Users;

namespace OrderFlow.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "orderflow";

    public string Audience { get; set; } = "orderflow-web";

    /// <summary>HMAC key, at least 32 bytes. Comes from user-secrets or the environment, never from committed files.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

internal sealed class PasswordService : IPasswordService
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Subject = new();
    private static readonly string DummyHash = Hasher.HashPassword(Subject, "dummy-password-for-timing");

    public string Hash(string password) => Hasher.HashPassword(Subject, password);

    public bool Verify(string passwordHash, string password) =>
        Hasher.VerifyHashedPassword(Subject, passwordHash, password) is not PasswordVerificationResult.Failed;

    public void SimulateVerification() => Hasher.VerifyHashedPassword(Subject, DummyHash, "wrong-password");
}

internal sealed class TokenService(IOptions<AuthOptions> options) : ITokenService
{
    private readonly AuthOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public AccessToken CreateAccessToken(User user, DateTimeOffset now)
    {
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                ["role"] = user.Role.ToString()
            },
            SigningCredentials = new SigningCredentials(_options.CreateKey(), SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public NewRefreshToken CreateRefreshToken()
    {
        var plain = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new NewRefreshToken(plain, HashRefreshToken(plain));
    }

    public string HashRefreshToken(string plainToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));
}

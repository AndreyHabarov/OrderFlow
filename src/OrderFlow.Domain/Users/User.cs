using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Users;

public enum UserRole
{
    Customer = 0,
    Admin = 1
}

public sealed class User
{
    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public UserRole Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Create(string email, string passwordHash, UserRole role, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        return new User
        {
            Id = Guid.NewGuid(),
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = now
        };
    }
}

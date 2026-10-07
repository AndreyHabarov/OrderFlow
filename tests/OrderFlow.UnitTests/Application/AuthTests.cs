using FluentValidation;
using OrderFlow.Application.Auth;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Users;

namespace OrderFlow.UnitTests.Application;

public class AuthTests
{
    private const string Password = "correct-horse-battery";

    private static async Task<AuthResponse> Register(TestApp app, string email = "Alice@Example.com") =>
        await app.Sender.Send(new RegisterCommand(email, Password));

    [Fact]
    public async Task Register_creates_a_customer_and_returns_tokens()
    {
        var app = new TestApp();

        var response = await Register(app);

        Assert.Equal("alice@example.com", response.User.Email);
        Assert.Equal(nameof(UserRole.Customer), response.User.Role);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.False(string.IsNullOrEmpty(response.RefreshToken));
        Assert.Single(app.Users.Store);
        Assert.NotEqual(Password, app.Users.Store[0].PasswordHash);
        Assert.Single(app.RefreshTokens.Store);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email_case_insensitively()
    {
        var app = new TestApp();
        await Register(app, "alice@example.com");

        await Assert.ThrowsAsync<ConflictException>(() => Register(app, "ALICE@example.com"));
    }

    [Theory]
    [InlineData("not-an-email", Password)]
    [InlineData("a@example.com", "short")]
    [InlineData("", Password)]
    public async Task Register_validates_email_and_password(string email, string password)
    {
        var app = new TestApp();

        await Assert.ThrowsAsync<ValidationException>(() => app.Sender.Send(new RegisterCommand(email, password)));
        Assert.Empty(app.Users.Store);
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        var app = new TestApp();
        await Register(app);

        var response = await app.Sender.Send(new LoginCommand("alice@example.com", Password));

        Assert.Equal("alice@example.com", response.User.Email);
        Assert.Equal(2, app.RefreshTokens.Store.Count);
    }

    [Fact]
    public async Task Login_fails_the_same_way_for_a_wrong_password_and_an_unknown_email()
    {
        var app = new TestApp();
        await Register(app);

        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedException>(
            () => app.Sender.Send(new LoginCommand("alice@example.com", "wrong-password")));
        var unknownEmail = await Assert.ThrowsAsync<UnauthorizedException>(
            () => app.Sender.Send(new LoginCommand("nobody@example.com", Password)));

        Assert.Equal(wrongPassword.Message, unknownEmail.Message);
        Assert.Equal(1, app.Passwords.Simulations);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_revokes_the_old_one()
    {
        var app = new TestApp();
        var first = await Register(app);

        var second = await app.Sender.Send(new RefreshTokenCommand(first.RefreshToken));

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        var old = app.RefreshTokens.Store[0];
        Assert.True(old.IsRevoked);
        Assert.Equal(app.RefreshTokens.Store[1].Id, old.ReplacedByTokenId);
        Assert.False(app.RefreshTokens.Store[1].IsRevoked);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_every_session_of_the_user()
    {
        var app = new TestApp();
        var first = await Register(app);
        var second = await app.Sender.Send(new RefreshTokenCommand(first.RefreshToken));

        // The attacker (or a buggy client) presents the already-rotated token again.
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Sender.Send(new RefreshTokenCommand(first.RefreshToken)));

        Assert.All(app.RefreshTokens.Store, t => Assert.True(t.IsRevoked));
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Sender.Send(new RefreshTokenCommand(second.RefreshToken)));
    }

    [Fact]
    public async Task Expired_and_unknown_refresh_tokens_are_rejected()
    {
        var app = new TestApp();
        var response = await Register(app);

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Sender.Send(new RefreshTokenCommand("never-issued")));

        app.Clock.Advance(TimeSpan.FromDays(15));
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Sender.Send(new RefreshTokenCommand(response.RefreshToken)));
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_is_silent_for_unknown_tokens()
    {
        var app = new TestApp();
        var response = await Register(app);

        await app.Sender.Send(new LogoutCommand("never-issued"));
        await app.Sender.Send(new LogoutCommand(response.RefreshToken));

        Assert.True(app.RefreshTokens.Store.Single().IsRevoked);
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Sender.Send(new RefreshTokenCommand(response.RefreshToken)));
    }
}

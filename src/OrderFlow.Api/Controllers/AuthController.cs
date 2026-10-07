using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Auth;

namespace OrderFlow.Api.Controllers;

public sealed record CredentialsRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResponse>> Register(CredentialsRequest request, CancellationToken cancellationToken)
    {
        var response = await sender.Send(new RegisterCommand(request.Email, request.Password), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(CredentialsRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken));

    /// <summary>Exchanges a refresh token for a new access token and a new refresh token (the old one is revoked).</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken));

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return NoContent();
    }
}

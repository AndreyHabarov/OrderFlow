using OrderFlow.Application.Common;

namespace OrderFlow.Api.Infrastructure;

/// <summary>Reads the customer id from the <c>sub</c> claim of the validated JWT.</summary>
internal sealed class ClaimsCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid CustomerId
    {
        get
        {
            var subject = accessor.HttpContext?.User.FindFirst("sub")?.Value;
            return Guid.TryParse(subject, out var id) && id != Guid.Empty
                ? id
                : throw new UnauthorizedException("A valid access token is required.");
        }
    }
}

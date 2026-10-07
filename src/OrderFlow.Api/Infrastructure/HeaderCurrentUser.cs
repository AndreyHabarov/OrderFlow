using OrderFlow.Application.Common;

namespace OrderFlow.Api.Infrastructure;

/// <summary>
/// TEMPORARY identity for stage 1 development: the customer id comes from the <c>X-Customer-Id</c> header.
/// Replaced by JWT claims in task S1-06; it must never be used outside local development.
/// </summary>
internal sealed class HeaderCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string HeaderName = "X-Customer-Id";

    public Guid CustomerId
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers[HeaderName].ToString();
            return Guid.TryParse(value, out var id) && id != Guid.Empty
                ? id
                : throw new UnauthorizedException($"Header {HeaderName} with a customer GUID is required.");
        }
    }
}

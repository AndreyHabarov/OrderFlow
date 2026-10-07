using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Orders;

namespace OrderFlow.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// Creates an order from the current cart. Requires an <c>Idempotency-Key</c> header: retrying with the same key
    /// returns the original order (200 with <c>Idempotent-Replayed: true</c>) instead of creating another one.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderDto>> Checkout(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CheckoutCommand(idempotencyKey ?? string.Empty), cancellationToken);
        if (result.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
            return Ok(result.Order);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Order.Id }, result.Order);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetMine(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyOrdersQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetOrderQuery(id), cancellationToken));
}

using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Orders;

namespace OrderFlow.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    /// <summary>Creates an order from the current cart.</summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDto>> Checkout(CancellationToken cancellationToken)
    {
        var order = await sender.Send(new CheckoutCommand(), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetMine(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyOrdersQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetOrderQuery(id), cancellationToken));
}

using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Carts;

namespace OrderFlow.Api.Controllers;

public sealed record AddCartItemRequest(Guid ProductId, int Quantity);

[ApiController]
[Route("api/cart")]
public sealed class CartController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CartDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCartQuery(), cancellationToken));

    [HttpPost("items")]
    public async Task<ActionResult<CartDto>> AddItem(AddCartItemRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new AddCartItemCommand(request.ProductId, request.Quantity), cancellationToken));

    [HttpDelete("items/{productId:guid}")]
    public async Task<ActionResult<CartDto>> RemoveItem(Guid productId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RemoveCartItemCommand(productId), cancellationToken));
}

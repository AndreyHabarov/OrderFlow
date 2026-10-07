using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Catalog;
using OrderFlow.Application.Common;

namespace OrderFlow.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    /// <summary>Paged product list (page size is clamped to 1..100).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetProductsQuery(page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await sender.Send(new GetProductQuery(id), cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}

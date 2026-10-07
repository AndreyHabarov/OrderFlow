using MediatR;
using OrderFlow.Application.Abstractions;

namespace OrderFlow.Application.Catalog;

/// <summary>Returns null when the product does not exist (the controller maps it to 404). Null results are not cached.</summary>
public sealed record GetProductQuery(Guid Id) : IRequest<ProductDto?>;

internal sealed class GetProductQueryHandler(IProductRepository products, ICacheService cache)
    : IRequestHandler<GetProductQuery, ProductDto?>
{
    public Task<ProductDto?> Handle(GetProductQuery request, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            CatalogCache.Namespace,
            $"item:{request.Id}",
            async ct =>
            {
                var product = await products.GetByIdAsync(request.Id, ct);
                return product is null ? null : ProductDto.From(product);
            },
            CatalogCache.TimeToLive,
            cancellationToken);
}

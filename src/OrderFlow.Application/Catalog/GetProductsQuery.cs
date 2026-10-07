using MediatR;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Common;

namespace OrderFlow.Application.Catalog;

public sealed record GetProductsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<ProductDto>>;

internal sealed class GetProductsQueryHandler(IProductRepository products, ICacheService cache)
    : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var result = await cache.GetOrCreateAsync(
            CatalogCache.Namespace,
            $"list:{page}:{pageSize}",
            async ct =>
            {
                var (items, total) = await products.GetPageAsync(page, pageSize, ct);
                return new PagedResult<ProductDto>(items.Select(ProductDto.From).ToList(), page, pageSize, total);
            },
            CatalogCache.TimeToLive,
            cancellationToken);

        return result!;
    }
}

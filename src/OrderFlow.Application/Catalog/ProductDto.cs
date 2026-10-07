using OrderFlow.Domain.Catalog;

namespace OrderFlow.Application.Catalog;

public sealed record ProductDto(Guid Id, string Name, string Description, decimal Price, string Currency)
{
    public static ProductDto From(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Price.Amount,
        product.Price.Currency);
}

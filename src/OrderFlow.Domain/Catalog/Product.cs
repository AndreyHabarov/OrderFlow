using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Catalog;

/// <summary>
/// A catalog entry: what the shop sells and for how much. Stock is NOT part of it: the Inventory service owns
/// quantities and reservations (ADR 0006), so the catalog can be read and cached without knowing availability.
/// </summary>
public sealed class Product
{
    private Product()
    {
        Name = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public Money Price { get; private set; }

    public static Product Create(string name, string description, Money price) =>
        Create(Guid.NewGuid(), name, description, price);

    /// <summary>With an explicit id: demo products share fixed ids with the Inventory service.</summary>
    public static Product Create(Guid id, string name, string description, Money price)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name is required.");
        }

        return new Product
        {
            Id = id,
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Price = price
        };
    }
}

using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Catalog;

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

    /// <summary>Units available for sale. In stage 2 this moves into the Inventory service.</summary>
    public int StockQuantity { get; private set; }

    public static Product Create(string name, string description, Money price, int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name is required.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainException("Stock quantity cannot be negative.");
        }

        return new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Price = price,
            StockQuantity = stockQuantity
        };
    }

    public void Reserve(int quantity)
    {
        EnsurePositive(quantity);
        if (quantity > StockQuantity)
        {
            throw new DomainException($"Not enough stock for '{Name}': requested {quantity}, available {StockQuantity}.");
        }

        StockQuantity -= quantity;
    }

    public void Release(int quantity)
    {
        EnsurePositive(quantity);
        StockQuantity += quantity;
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be positive.");
        }
    }
}

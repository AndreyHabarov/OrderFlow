using OrderFlow.Domain.Common;

namespace OrderFlow.Domain.Carts;

public sealed class Cart
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    public Money Total => _items.Aggregate(Money.Zero(), (sum, item) => sum.Add(item.LineTotal));

    public static Cart CreateFor(Guid customerId)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainException("Customer id is required.");
        }

        return new Cart { Id = Guid.NewGuid(), CustomerId = customerId };
    }

    public void AddItem(Guid productId, int quantity, Money unitPrice)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be positive.");
        }

        var existing = _items.FindIndex(i => i.ProductId == productId);
        if (existing >= 0)
        {
            var item = _items[existing];
            _items[existing] = item with { Quantity = item.Quantity + quantity, UnitPrice = unitPrice };
            return;
        }

        _items.Add(new CartItem(productId, quantity, unitPrice));
    }

    public void RemoveItem(Guid productId)
    {
        if (_items.RemoveAll(i => i.ProductId == productId) == 0)
        {
            throw new DomainException("Product is not in the cart.");
        }
    }

    public void Clear() => _items.Clear();
}

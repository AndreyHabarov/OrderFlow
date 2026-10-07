namespace OrderFlow.Contracts.Demo;

/// <summary>
/// Sample products with fixed ids for local development. Orders seeds the catalog (name, price) and Inventory seeds
/// the stock for the same ids, so both services agree on product identity without calling each other.
/// </summary>
public static class DemoCatalog
{
    public sealed record DemoProduct(Guid Id, string Name, string Description, decimal Price, int Stock);

    public static IReadOnlyList<DemoProduct> Products { get; } =
    [
        new(new Guid("a1000000-0000-0000-0000-000000000001"), "Mechanical keyboard", "Hot-swappable, 75% layout", 89.90m, 25),
        new(new Guid("a1000000-0000-0000-0000-000000000002"), "Wireless mouse", "Ergonomic, 2.4 GHz and Bluetooth", 39.50m, 40),
        new(new Guid("a1000000-0000-0000-0000-000000000003"), "27\" monitor", "QHD IPS, 144 Hz", 329.00m, 10),
        new(new Guid("a1000000-0000-0000-0000-000000000004"), "USB-C dock", "8-in-1, 100 W power delivery", 74.00m, 30),
        new(new Guid("a1000000-0000-0000-0000-000000000005"), "Laptop stand", "Aluminium, adjustable height", 29.99m, 50),
        new(new Guid("a1000000-0000-0000-0000-000000000006"), "Noise-cancelling headphones", "40 h battery", 199.00m, 15),
        new(new Guid("a1000000-0000-0000-0000-000000000007"), "Webcam 1080p", "Autofocus, built-in microphone", 59.00m, 20),
        new(new Guid("a1000000-0000-0000-0000-000000000008"), "Desk mat", "XL, non-slip", 19.90m, 60)
    ];
}

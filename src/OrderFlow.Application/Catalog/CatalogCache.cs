namespace OrderFlow.Application.Catalog;

public static class CatalogCache
{
    /// <summary>Invalidate this namespace whenever product data (price, stock, name) changes.</summary>
    public const string Namespace = "catalog";

    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);
}

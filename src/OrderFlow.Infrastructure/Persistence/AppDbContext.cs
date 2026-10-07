using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Carts;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Orders;
using OrderFlow.Domain.Users;

namespace OrderFlow.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    /// <summary>Schema owned by the Orders service. Inventory and Payments get their own schemas in stage 2.</summary>
    public const string Schema = "orders";

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public bool IsUniqueViolation(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

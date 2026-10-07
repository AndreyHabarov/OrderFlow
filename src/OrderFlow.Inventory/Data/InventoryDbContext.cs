using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Inventory.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    /// <summary>This service's own schema. It never reads tables of other services.</summary>
    public const string Schema = "inventory";

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<StockItem>(item =>
        {
            item.ToTable("stock_items");
            item.HasKey(s => s.ProductId);
            item.Property(s => s.ProductId).ValueGeneratedNever();
            item.Property(s => s.Available).IsRequired();

            // Optimistic concurrency: two consumers changing the same stock row cannot both win (PostgreSQL xmin).
            item.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Reservation>(reservation =>
        {
            reservation.ToTable("reservations");
            reservation.HasKey(r => r.OrderId);
            reservation.Property(r => r.OrderId).ValueGeneratedNever();
            reservation.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            reservation.Property(r => r.TotalAmount).HasPrecision(18, 2);
            reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            reservation.Property(r => r.FailureReason).HasMaxLength(500);
            reservation.HasMany(r => r.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            reservation.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ReservationLine>(line =>
        {
            line.ToTable("reservation_lines");
            line.Property<Guid>("OrderId");
            line.HasKey("OrderId", nameof(ReservationLine.ProductId));
        });
    }
}

/// <summary>Used only by <c>dotnet ef</c> to create migrations; no database connection is opened.</summary>
internal sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=localhost;Database=orderflow_design", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new InventoryDbContext(options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderFlow.Payments.Data;

public enum PaymentStatus
{
    Succeeded = 0,
    Failed = 1
}

/// <summary>
/// The result of charging for one order, keyed by order id. Saved before the answer is published, so a redelivered
/// <c>StockReserved</c> finds it and repeats the answer instead of charging the customer twice.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
        Currency = string.Empty;
    }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Payment Succeeded(Guid orderId, Guid customerId, decimal amount, string currency, DateTimeOffset now) =>
        new() { OrderId = orderId, CustomerId = customerId, Amount = amount, Currency = currency, Status = PaymentStatus.Succeeded, CreatedAt = now };

    public static Payment Failed(Guid orderId, Guid customerId, decimal amount, string currency, string reason, DateTimeOffset now) =>
        new() { OrderId = orderId, CustomerId = customerId, Amount = amount, Currency = currency, Status = PaymentStatus.Failed, FailureReason = reason, CreatedAt = now };
}

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    /// <summary>This service's own schema. It never reads tables of other services.</summary>
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.OrderId);
            payment.Property(p => p.OrderId).ValueGeneratedNever();
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            payment.Property(p => p.FailureReason).HasMaxLength(500);
        });
    }
}

/// <summary>Used only by <c>dotnet ef</c> to create migrations; no database connection is opened.</summary>
internal sealed class PaymentsDesignTimeFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=localhost;Database=orderflow_design", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PaymentsDbContext(options);
    }
}

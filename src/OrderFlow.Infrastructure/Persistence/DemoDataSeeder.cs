using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Auth;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Users;

namespace OrderFlow.Infrastructure.Persistence;

public static class DemoDataSeeder
{
    /// <summary>
    /// Local development only. Inserts sample products into an empty catalog and, when <c>Seed:AdminEmail</c> and
    /// <c>Seed:AdminPassword</c> are configured, an administrator account. Idempotent.
    /// </summary>
    public static async Task SeedDemoDataAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (!await db.Products.AnyAsync(cancellationToken))
        {
            db.Products.AddRange(
                Product.Create("Mechanical keyboard", "Hot-swappable, 75% layout", new Money(89.90m), 25),
                Product.Create("Wireless mouse", "Ergonomic, 2.4 GHz and Bluetooth", new Money(39.50m), 40),
                Product.Create("27\" monitor", "QHD IPS, 144 Hz", new Money(329.00m), 10),
                Product.Create("USB-C dock", "8-in-1, 100 W power delivery", new Money(74.00m), 30),
                Product.Create("Laptop stand", "Aluminium, adjustable height", new Money(29.99m), 50),
                Product.Create("Noise-cancelling headphones", "40 h battery", new Money(199.00m), 15),
                Product.Create("Webcam 1080p", "Autofocus, built-in microphone", new Money(59.00m), 20),
                Product.Create("Desk mat", "XL, non-slip", new Money(19.90m), 60));
        }

        var adminEmail = configuration["Seed:AdminEmail"];
        var adminPassword = configuration["Seed:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var normalized = User.NormalizeEmail(adminEmail);
            if (!await db.Users.AnyAsync(u => u.Email == normalized, cancellationToken))
            {
                var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
                var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                db.Users.Add(User.Create(normalized, passwords.Hash(adminPassword), UserRole.Admin, timeProvider.GetUtcNow()));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

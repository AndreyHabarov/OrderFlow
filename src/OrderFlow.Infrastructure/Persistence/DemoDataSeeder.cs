using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Auth;
using OrderFlow.Contracts.Demo;
using OrderFlow.Domain.Catalog;
using OrderFlow.Domain.Common;
using OrderFlow.Domain.Users;

namespace OrderFlow.Infrastructure.Persistence;

public static class DemoDataSeeder
{
    /// <summary>
    /// Local development only. Makes the catalog contain exactly the shared demo products (fixed ids, so the Inventory
    /// service can seed stock for the same products) and, when <c>Seed:AdminEmail</c> and <c>Seed:AdminPassword</c> are
    /// configured, an administrator account. Idempotent.
    /// </summary>
    public static async Task SeedDemoDataAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var demoIds = DemoCatalog.Products.Select(product => product.Id).ToList();
        var existing = await db.Products.ToListAsync(cancellationToken);
        if (!demoIds.All(id => existing.Any(product => product.Id == id)) || existing.Count != demoIds.Count)
        {
            // An older catalog (random ids from before stage 2) would point at products Inventory has never heard of.
            db.Products.RemoveRange(existing);
            db.Products.AddRange(DemoCatalog.Products.Select(p => Product.Create(p.Id, p.Name, p.Description, new Money(p.Price))));
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

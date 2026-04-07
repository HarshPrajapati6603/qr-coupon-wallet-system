using LoyaltyPlatform.Domain.Entities;
using LoyaltyPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LoyaltyPlatform.Infrastructure.Data;

/// <summary>Seeds a default admin user on first run.</summary>
public static class SeedData
{
    public static async Task SeedAsync(LoyaltyDbContext db, IConfiguration config)
    {
        var adminEmail = config["AdminSeed:Email"] ?? "admin@loyalty.com";
        var adminPassword = config["AdminSeed:Password"] ?? "Admin@123!";
        var adminUsername = config["AdminSeed:Username"] ?? "admin";

        if (await db.Users.AnyAsync(u => u.Email == adminEmail))
            return;

        var admin = new User
        {
            Username = adminUsername,
            Email = adminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Role = "Admin"
        };

        db.Users.Add(admin);
        db.Wallets.Add(new Wallet { UserId = admin.Id });

        await db.SaveChangesAsync();
        Console.WriteLine($"[Seed] Admin user created: {adminEmail}");
    }
}

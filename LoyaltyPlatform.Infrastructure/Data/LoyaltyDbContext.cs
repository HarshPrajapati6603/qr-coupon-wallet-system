using LoyaltyPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LoyaltyPlatform.Infrastructure.Data;

public class LoyaltyDbContext : DbContext
{
    public LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── User ─────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Role).HasMaxLength(20).HasDefaultValue("User");
            e.Property(u => u.PasswordHash).HasMaxLength(512);
            e.Property(u => u.Username).HasMaxLength(100);
            e.Property(u => u.Email).HasMaxLength(200);
        });

        // ── Wallet ────────────────────────────────────────────────
        modelBuilder.Entity<Wallet>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.UserId).IsUnique();
            e.Property(w => w.Balance).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            // DB-level constraint: balance can never go negative
            e.ToTable(t => t.HasCheckConstraint("CK_Wallet_Balance_NonNegative", "[Balance] >= 0"));
            e.HasOne(w => w.User)
             .WithOne(u => u.Wallet)
             .HasForeignKey<Wallet>(w => w.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Campaign ──────────────────────────────────────────────
        modelBuilder.Entity<Campaign>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Description).HasMaxLength(1000);
            e.Property(c => c.RewardValue).HasColumnType("decimal(18,2)");
        });

        // ── Coupon ────────────────────────────────────────────────
        modelBuilder.Entity<Coupon>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(100);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(c => c.Campaign)
             .WithMany(camp => camp.Coupons)
             .HasForeignKey(c => c.CampaignId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.RedeemedByUser)
             .WithMany(u => u.RedeemedCoupons)
             .HasForeignKey(c => c.RedeemedByUserId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        // ── Transaction ───────────────────────────────────────────
        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.IdempotencyKey).IsUnique();
            e.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.IdempotencyKey).HasMaxLength(200);
            e.HasOne(t => t.Wallet)
             .WithMany(w => w.Transactions)
             .HasForeignKey(t => t.WalletId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Coupon)
             .WithOne(c => c.Transaction)
             .HasForeignKey<Transaction>(t => t.CouponId)
             .OnDelete(DeleteBehavior.Restrict)
             .IsRequired(false);
        });
    }
}

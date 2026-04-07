using LoyaltyPlatform.Application.DTOs;
using LoyaltyPlatform.Application.Interfaces;
using LoyaltyPlatform.Domain.Entities;
using LoyaltyPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LoyaltyPlatform.Infrastructure.Services;

/// <summary>
/// Detects and repairs partial failures in the redemption flow.
/// 
/// A "partial failure" is when:
///   - A Transaction record exists with Status = PartialFailure
///   - The Wallet was credited (Balance already includes the reward)
///   - But the Coupon is still marked Active (not Redeemed)
///
/// The reconciler safely marks the Coupon as Redeemed and the Transaction as Completed.
/// It does NOT re-credit the wallet (wallet already has the money).
///
/// IMPORTANT: The reconciler is idempotent — running it multiple times is safe.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly LoyaltyDbContext _db;

    public ReconciliationService(LoyaltyDbContext db)
    {
        _db = db;
    }

    public async Task<List<ReconciliationIssueDto>> PreviewAsync()
    {
        return await FindIssuesAsync();
    }

    public async Task<ReconciliationSummaryDto> ReconcileAsync()
    {
        var issues = await FindIssuesAsync();

        if (issues.Count == 0)
            return new ReconciliationSummaryDto(0, 0, new List<ReconciliationIssueDto>());

        var fixedIssues = new List<ReconciliationIssueDto>();

        // Process each issue in its own transaction for safety
        foreach (var issue in issues)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var transaction = await _db.Transactions.FindAsync(issue.TransactionId);
                if (transaction is null || transaction.Status == TransactionStatus.Completed)
                {
                    // Already fixed by another reconcile run — skip
                    await tx.RollbackAsync();
                    return;
                }

                var coupon = await _db.Coupons.FindAsync(issue.CouponId);
                if (coupon is null)
                {
                    await tx.RollbackAsync();
                    return;
                }

                // Extract userId from idempotency key: "userId:couponCode"
                var parts = transaction.IdempotencyKey.Split(':', 2);
                if (parts.Length == 2 && Guid.TryParse(parts[0], out var userId))
                {
                    // Fix coupon status — do NOT adjust wallet (already credited)
                    coupon.Status = CouponStatus.Redeemed;
                    coupon.RedeemedByUserId = userId;
                    coupon.RedeemedAt = DateTime.UtcNow;
                }

                // Mark transaction as complete
                transaction.Status = TransactionStatus.Completed;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                fixedIssues.Add(issue);
            }
            catch
            {
                await tx.RollbackAsync();
                // Log and continue — don't let one bad record block all reconciliation
            }
            });
        }

        return new ReconciliationSummaryDto(issues.Count, fixedIssues.Count, fixedIssues);
    }

    private async Task<List<ReconciliationIssueDto>> FindIssuesAsync()
    {
        // Find all partial failure transactions that have an associated coupon
        // that is STILL Active (meaning the coupon was never marked Redeemed).
        var partialFailures = await _db.Transactions
            .Include(t => t.Coupon)
            .Where(t =>
                t.Status == TransactionStatus.PartialFailure &&
                t.CouponId != null &&
                t.Coupon!.Status == CouponStatus.Active)
            .ToListAsync();

        return partialFailures.Select(t => new ReconciliationIssueDto(
            TransactionId: t.Id,
            CouponId: t.CouponId!.Value,
            CouponCode: t.Coupon!.Code,
            WalletId: t.WalletId,
            Amount: t.Amount,
            IssueDescription: $"Wallet credited for {t.Amount:C} but coupon '{t.Coupon.Code}' still shows Active. Transaction was PartialFailure."))
            .ToList();
    }
}

using LoyaltyPlatform.Application.DTOs;
using LoyaltyPlatform.Application.Interfaces;
using LoyaltyPlatform.Domain.Entities;
using LoyaltyPlatform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LoyaltyPlatform.Infrastructure.Services;

/// <summary>
/// Handles coupon redemption with:
/// 1. Idempotency: checks for existing transaction by idempotency key before processing
/// 2. Concurrency: uses SELECT ... WITH (UPDLOCK, ROWLOCK) inside Serializable transaction
///    so only one concurrent request can redeem a given coupon
/// 3. Partial failure tracking: Transaction.Status starts as PartialFailure,
///    only set to Completed at the very end — reconciler detects incomplete ones
/// </summary>
public class RedemptionService : IRedemptionService
{
    private readonly LoyaltyDbContext _db;

    public RedemptionService(LoyaltyDbContext db)
    {
        _db = db;
    }

    public async Task<RedemptionResultDto> RedeemCouponAsync(Guid userId, string couponCode)
    {
        // Build idempotency key scoped to this user + this coupon code
        var idempotencyKey = $"{userId}:{couponCode}";

        // ── Step 1: Idempotency check (outside main transaction, read-committed) ──
        // If we already have a COMPLETED transaction for this key, return the cached result.
        var existingTx = await _db.Transactions
            .Include(t => t.Wallet)
            .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey && t.Status == TransactionStatus.Completed);

        if (existingTx != null)
        {
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == existingTx.WalletId);
            return new RedemptionResultDto(
                Success: true,
                Message: "Already redeemed (idempotent response).",
                NewBalance: wallet?.Balance,
                RewardAmount: existingTx.Amount,
                TransactionId: existingTx.Id);
        }

        // ── Step 2: Serializable transaction with row-level locking ──────────────
        // Serializable prevents phantom reads. UPDLOCK prevents two concurrent 
        // readers from both seeing Status=Active and both proceeding to redeem.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var dbTransaction = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

        try
        {
            // Lock the coupon row — only one transaction holder can proceed
            var coupon = await _db.Coupons
                .FromSql($@"
                    SELECT * FROM Coupons WITH (UPDLOCK, ROWLOCK) 
                    WHERE Code = {couponCode}")
                .Include(c => c.Campaign)
                .FirstOrDefaultAsync();

            if (coupon is null)
            {
                await dbTransaction.RollbackAsync();
                return new RedemptionResultDto(false, "Coupon not found.", null, null, null);
            }

            // ── Validation ────────────────────────────────────────────────────────
            if (coupon.Status == CouponStatus.Redeemed)
            {
                await dbTransaction.RollbackAsync();
                return new RedemptionResultDto(false, "Coupon has already been redeemed.", null, null, null);
            }

            if (coupon.Status == CouponStatus.Expired)
            {
                await dbTransaction.RollbackAsync();
                return new RedemptionResultDto(false, "Coupon is expired.", null, null, null);
            }

            if (!coupon.Campaign!.IsActive)
            {
                await dbTransaction.RollbackAsync();
                return new RedemptionResultDto(false, "Campaign is no longer active.", null, null, null);
            }

            if (coupon.Campaign.ExpiresAt.HasValue && coupon.Campaign.ExpiresAt.Value < DateTime.UtcNow)
            {
                await dbTransaction.RollbackAsync();
                return new RedemptionResultDto(false, "Campaign has expired.", null, null, null);
            }

            // ── Get the user's wallet (lock it too) ───────────────────────────────
            var wallet = await _db.Wallets
                .FromSql($"SELECT * FROM Wallets WITH (UPDLOCK, ROWLOCK) WHERE UserId = {userId}")
                .FirstOrDefaultAsync();

            if (wallet is null)
            {
                await dbTransaction.RollbackAsync();
                return new RedemptionResultDto(false, "Wallet not found for user.", null, null, null);
            }

            var rewardAmount = coupon.Campaign.RewardValue;

            // ── Step 3: Write Transaction record as PartialFailure ─────────────────
            // This is the "write-ahead" record. If we crash here, reconciler sees it.
            var transaction = new Transaction
            {
                WalletId = wallet.Id,
                CouponId = coupon.Id,
                Amount = rewardAmount,
                Type = TransactionType.Credit,
                IdempotencyKey = idempotencyKey,
                Status = TransactionStatus.PartialFailure  // Will be updated at the end
            };
            _db.Transactions.Add(transaction);
            await _db.SaveChangesAsync();

            // ── Step 4: Update wallet balance ──────────────────────────────────────
            wallet.Balance += rewardAmount;
            wallet.UpdatedAt = DateTime.UtcNow;

            // ── Step 5: Mark coupon as redeemed ────────────────────────────────────
            coupon.Status = CouponStatus.Redeemed;
            coupon.RedeemedByUserId = userId;
            coupon.RedeemedAt = DateTime.UtcNow;

            // ── Step 6: Mark Transaction as Completed ──────────────────────────────
            // This is the "success marker". After commit, reconciler will NOT touch this.
            transaction.Status = TransactionStatus.Completed;

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            return new RedemptionResultDto(
                Success: true,
                Message: "Coupon redeemed successfully.",
                NewBalance: wallet.Balance,
                RewardAmount: rewardAmount,
                TransactionId: transaction.Id);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Another concurrent request already inserted the idempotency key — safe to return conflict
            await dbTransaction.RollbackAsync();
            return new RedemptionResultDto(false, "Coupon is being processed concurrently. Please try again.", null, null, null);
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
        });
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}

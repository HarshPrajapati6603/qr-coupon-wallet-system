#if DEBUG
using System.Security.Claims;
using LoyaltyPlatform.Domain.Entities;
using LoyaltyPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoyaltyPlatform.API.Controllers;

/// <summary>
/// TEST ONLY — not compiled in Release builds.
///
/// Simulates a "partial failure" by:
/// 1. Crediting the wallet
/// 2. Writing a Transaction with Status=PartialFailure
/// 3. Intentionally NOT marking the Coupon as Redeemed
///
/// This mimics a crash between steps 5 and 6 of the redemption flow.
/// After calling this, use GET /api/admin/reconcile/preview to see the issue,
/// then POST /api/admin/reconcile to fix it.
/// </summary>
[ApiController]
[Route("api/test")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class TestController : ControllerBase
{
    private readonly LoyaltyDbContext _db;

    public TestController(LoyaltyDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Simulates a partial failure for the given coupon code.
    /// The wallet gets credited, but the coupon stays Active.
    /// </summary>
    [HttpPost("simulate-partial-failure")]
    public async Task<IActionResult> SimulatePartialFailure([FromBody] SimulateRequest request)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var dbTx = await _db.Database.BeginTransactionAsync();

            try
            {
                var coupon = await _db.Coupons
                    .Include(c => c.Campaign)
                    .FirstOrDefaultAsync(c => c.Code == request.CouponCode)
                    ?? throw new KeyNotFoundException($"Coupon '{request.CouponCode}' not found.");

                if (coupon.Status != CouponStatus.Active)
                    return BadRequest(new { error = "Coupon must be Active to simulate partial failure." });

                var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

                var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId)
                    ?? throw new KeyNotFoundException("Wallet not found.");

                var rewardAmount = coupon.Campaign!.RewardValue;

                wallet.Balance += rewardAmount;
                wallet.UpdatedAt = DateTime.UtcNow;

                var transaction = new Transaction
                {
                    WalletId = wallet.Id,
                    CouponId = coupon.Id,
                    Amount = rewardAmount,
                    Type = TransactionType.Credit,
                    IdempotencyKey = $"{userId}:{request.CouponCode}",
                    Status = TransactionStatus.PartialFailure
                };

                _db.Transactions.Add(transaction);

                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();

                return (IActionResult)Ok(new
                {
                    message = "Partial failure simulated successfully.",
                    transactionId = transaction.Id
                });
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync();
                return (IActionResult)BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record SimulateRequest(string CouponCode);
#endif

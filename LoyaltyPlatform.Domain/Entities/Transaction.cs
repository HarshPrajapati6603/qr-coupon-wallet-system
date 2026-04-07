namespace LoyaltyPlatform.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WalletId { get; set; }
    public Guid? CouponId { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    
    /// <summary>
    /// Prevents double-processing. Composed as "userId:couponCode" on the server side.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Written as PartialFailure first. Updated to Completed at the very end of the transaction.
    /// Allows the reconciler to detect crashed mid-flight operations.
    /// </summary>
    public TransactionStatus Status { get; set; } = TransactionStatus.PartialFailure;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Wallet? Wallet { get; set; }
    public Coupon? Coupon { get; set; }
}

public enum TransactionType
{
    Credit,
    Debit
}

public enum TransactionStatus
{
    Completed,
    PartialFailure
}

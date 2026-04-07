namespace LoyaltyPlatform.Domain.Entities;

public class Coupon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public string Code { get; set; } = string.Empty;
    public CouponStatus Status { get; set; } = CouponStatus.Active;
    public Guid? RedeemedByUserId { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Campaign? Campaign { get; set; }
    public User? RedeemedByUser { get; set; }
    public Transaction? Transaction { get; set; }
}

public enum CouponStatus
{
    Active,
    Redeemed,
    Expired
}

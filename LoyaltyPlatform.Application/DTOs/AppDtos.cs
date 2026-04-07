namespace LoyaltyPlatform.Application.DTOs;

// ── Auth ──────────────────────────────────────────────────
public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, string Role);

// ── Wallet ────────────────────────────────────────────────
public record WalletDto(Guid WalletId, Guid UserId, decimal Balance, DateTime UpdatedAt);

// ── Campaign ──────────────────────────────────────────────
public record CreateCampaignRequest(
    string Name,
    string Description,
    decimal RewardValue,
    DateTime StartsAt,
    DateTime? ExpiresAt);

public record CampaignDto(
    Guid Id,
    string Name,
    string Description,
    decimal RewardValue,
    DateTime StartsAt,
    DateTime? ExpiresAt,
    bool IsActive);

// ── Coupon ────────────────────────────────────────────────
public record CouponDto(Guid Id, string Code, string Status, Guid CampaignId);

// ── Redemption ────────────────────────────────────────────
public record RedeemRequest(string CouponCode);

public record RedemptionResultDto(
    bool Success,
    string Message,
    decimal? NewBalance,
    decimal? RewardAmount,
    Guid? TransactionId);

// ── Reconciliation ────────────────────────────────────────
public record ReconciliationIssueDto(
    Guid TransactionId,
    Guid CouponId,
    string CouponCode,
    Guid WalletId,
    decimal Amount,
    string IssueDescription);

public record ReconciliationSummaryDto(
    int IssuesFound,
    int IssuesFixed,
    List<ReconciliationIssueDto> FixedIssues);

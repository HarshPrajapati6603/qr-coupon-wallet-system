using LoyaltyPlatform.Application.DTOs;

namespace LoyaltyPlatform.Application.Interfaces;

public interface IRedemptionService
{
    Task<RedemptionResultDto> RedeemCouponAsync(Guid userId, string couponCode);
}

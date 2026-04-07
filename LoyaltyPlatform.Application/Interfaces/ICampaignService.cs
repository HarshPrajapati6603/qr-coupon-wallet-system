using LoyaltyPlatform.Application.DTOs;

namespace LoyaltyPlatform.Application.Interfaces;

public interface ICampaignService
{
    Task<CampaignDto> CreateCampaignAsync(CreateCampaignRequest request);
    Task<List<CouponDto>> GenerateCouponsAsync(Guid campaignId, int count);
}

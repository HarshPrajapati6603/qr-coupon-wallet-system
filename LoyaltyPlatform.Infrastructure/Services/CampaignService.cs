using LoyaltyPlatform.Application.DTOs;
using LoyaltyPlatform.Application.Interfaces;
using LoyaltyPlatform.Domain.Entities;
using LoyaltyPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LoyaltyPlatform.Infrastructure.Services;

public class CampaignService : ICampaignService
{
    private readonly LoyaltyDbContext _db;

    public CampaignService(LoyaltyDbContext db)
    {
        _db = db;
    }

    public async Task<CampaignDto> CreateCampaignAsync(CreateCampaignRequest request)
    {
        var campaign = new Campaign
        {
            Name = request.Name,
            Description = request.Description,
            RewardValue = request.RewardValue,
            StartsAt = request.StartsAt,
            ExpiresAt = request.ExpiresAt,
            IsActive = true
        };

        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        return MapToDto(campaign);
    }

    public async Task<List<CouponDto>> GenerateCouponsAsync(Guid campaignId, int count)
    {
        var campaign = await _db.Campaigns.FindAsync(campaignId)
            ?? throw new KeyNotFoundException($"Campaign {campaignId} not found.");

        if (!campaign.IsActive)
            throw new InvalidOperationException("Cannot generate coupons for an inactive campaign.");

        if (count <= 0 || count > 1000)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 1000.");

        var coupons = new List<Coupon>(count);
        for (int i = 0; i < count; i++)
        {
            // Generate URL-safe Base64 code from GUID — collision-resistant, non-guessable
            var code = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            coupons.Add(new Coupon
            {
                CampaignId = campaignId,
                Code = code,
                Status = CouponStatus.Active
            });
        }

        _db.Coupons.AddRange(coupons);
        await _db.SaveChangesAsync();

        return coupons.Select(c => new CouponDto(c.Id, c.Code, c.Status.ToString(), c.CampaignId)).ToList();
    }

    private static CampaignDto MapToDto(Campaign c) =>
        new(c.Id, c.Name, c.Description, c.RewardValue, c.StartsAt, c.ExpiresAt, c.IsActive);
}

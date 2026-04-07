using System.Security.Claims;
using LoyaltyPlatform.Application.DTOs;
using LoyaltyPlatform.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyaltyPlatform.API.Controllers;

[ApiController]
[Route("api/coupons")]
[Authorize]
[Produces("application/json")]
public class CouponController : ControllerBase
{
    private readonly IRedemptionService _redemptionService;

    public CouponController(IRedemptionService redemptionService)
    {
        _redemptionService = redemptionService;
    }

    /// <summary>
    /// Redeems a coupon by code. Idempotent — safe to retry with the same coupon code.
    /// Returns 409 if the coupon is already redeemed by another user/request.
    /// </summary>
    [HttpPost("redeem")]
    [ProducesResponseType(typeof(RedemptionResultDto), 200)]
    [ProducesResponseType(typeof(RedemptionResultDto), 409)]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Redeem([FromBody] RedeemRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _redemptionService.RedeemCouponAsync(userId, request.CouponCode);

        if (result.Success)
            return Ok(result);

        // Distinguish between "already redeemed" (409) vs "not found" (404) vs other validation (400)
        if (result.Message.Contains("already been redeemed") || result.Message.Contains("concurrently"))
            return Conflict(result);

        if (result.Message.Contains("not found"))
            return NotFound(result);

        return BadRequest(result);
    }
}

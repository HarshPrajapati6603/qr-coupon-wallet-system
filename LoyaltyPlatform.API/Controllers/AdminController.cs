using LoyaltyPlatform.Application.DTOs;
using LoyaltyPlatform.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoyaltyPlatform.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly IReconciliationService _reconciliationService;

    public AdminController(ICampaignService campaignService, IReconciliationService reconciliationService)
    {
        _campaignService = campaignService;
        _reconciliationService = reconciliationService;
    }

    // ── Campaign Endpoints ────────────────────────────────────────────────────

    /// <summary>Creates a new loyalty campaign.</summary>
    [HttpPost("campaigns")]
    [ProducesResponseType(typeof(CampaignDto), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        try
        {
            var campaign = await _campaignService.CreateCampaignAsync(request);
            return CreatedAtAction(nameof(CreateCampaign), new { id = campaign.Id }, campaign);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Generates N unique coupon codes for a campaign.</summary>
    [HttpPost("campaigns/{id:guid}/coupons")]
    [ProducesResponseType(typeof(List<CouponDto>), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GenerateCoupons(Guid id, [FromQuery] int count = 10)
    {
        try
        {
            var coupons = await _campaignService.GenerateCouponsAsync(id, count);
            return StatusCode(201, coupons);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Reconciliation Endpoints ──────────────────────────────────────────────

    /// <summary>
    /// Dry-run: returns all detected partial failures without fixing them.
    /// Safe to call at any time.
    /// </summary>
    [HttpGet("reconcile/preview")]
    [ProducesResponseType(typeof(List<ReconciliationIssueDto>), 200)]
    public async Task<IActionResult> PreviewReconciliation()
    {
        var issues = await _reconciliationService.PreviewAsync();
        return Ok(new { totalIssues = issues.Count, issues });
    }

    /// <summary>
    /// Applies reconciliation fixes: marks PartialFailure transactions as Completed
    /// and updates Coupon status to Redeemed. Does NOT re-credit wallets.
    /// Idempotent — safe to run multiple times.
    /// </summary>
    [HttpPost("reconcile")]
    [ProducesResponseType(typeof(ReconciliationSummaryDto), 200)]
    public async Task<IActionResult> Reconcile()
    {
        var summary = await _reconciliationService.ReconcileAsync();
        return Ok(summary);
    }
}

using LoyaltyPlatform.Application.DTOs;

namespace LoyaltyPlatform.Application.Interfaces;

public interface IReconciliationService
{
    Task<List<ReconciliationIssueDto>> PreviewAsync();
    Task<ReconciliationSummaryDto> ReconcileAsync();
}

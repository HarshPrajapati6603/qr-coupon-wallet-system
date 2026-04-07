using LoyaltyPlatform.Application.DTOs;

namespace LoyaltyPlatform.Application.Interfaces;

public interface IWalletService
{
    Task<WalletDto> GetBalanceAsync(Guid userId);
}

using LoyaltyPlatform.Application.DTOs;
using LoyaltyPlatform.Application.Interfaces;
using LoyaltyPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LoyaltyPlatform.Infrastructure.Services;

public class WalletService : IWalletService
{
    private readonly LoyaltyDbContext _db;

    public WalletService(LoyaltyDbContext db)
    {
        _db = db;
    }

    public async Task<WalletDto> GetBalanceAsync(Guid userId)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId)
            ?? throw new KeyNotFoundException("Wallet not found for this user.");

        return new WalletDto(wallet.Id, wallet.UserId, wallet.Balance, wallet.UpdatedAt);
    }
}

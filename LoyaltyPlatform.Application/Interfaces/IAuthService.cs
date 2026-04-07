using LoyaltyPlatform.Domain.Entities;

namespace LoyaltyPlatform.Application.Interfaces;

public interface IAuthService
{
    Task<string> RegisterAsync(string username, string email, string password, string role = "User");
    Task<(string,string)> LoginAsync(string email, string password);
}

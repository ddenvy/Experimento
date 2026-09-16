using Experimento.Domain.Entities;

namespace Experimento.Application.Abstractions;

/// <summary>
/// Authentication service: register, login, refresh, logout on BCrypt + JWT.
/// </summary>
public interface IAuthService
{
    Task<(User User, string AccessToken, string RefreshToken)> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default);
    Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<(string AccessToken, string NewRefreshToken)> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}

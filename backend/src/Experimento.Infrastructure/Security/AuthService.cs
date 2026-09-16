using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Experimento.Infrastructure.Security;

/// <summary>
/// Authentication service using BCrypt for passwords and JWT access + refresh tokens.
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    public AuthService(AppDbContext db, IConfiguration config) => (_db, _config) = (db, config);

    public async Task<(User User, string AccessToken, string RefreshToken)> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default)
    {
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("User with this email already exists.");

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = displayName
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        var (access, refresh) = await IssueTokensAsync(user, ct);
        return (user, access, refresh);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
                   ?? throw new UnauthorizedAccessException("Invalid credentials.");
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        var (access, refresh) = await IssueTokensAsync(user, ct);
        return (user, access, refresh);
    }

    public async Task<(string AccessToken, string NewRefreshToken)> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (!token.IsActive)
        {
            // Предъявлен уже ротированный (отозванный) токен — вероятный признак кражи
            // и повторного использования (token reuse): отзываем всё семейство токенов пользователя.
            if (token.RevokedAtUtc is not null)
                await RevokeAllUserTokensAsync(token.UserId, ct);
            throw new UnauthorizedAccessException("Refresh token expired or revoked.");
        }

        token.RevokedAtUtc = DateTime.UtcNow;
        var (access, newRefresh) = await IssueTokensAsync(token.User, ct);
        return (access, newRefresh);
    }

    private async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var t in active)
            t.RevokedAtUtc = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<(string Access, string Refresh)> IssueTokensAsync(User user, CancellationToken ct)
    {
        var access = GenerateAccessToken(user);
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(refresh),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });
        await _db.SaveChangesAsync(ct);
        return (access, refresh);
    }

    private string GenerateAccessToken(User user)
    {
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured.");
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "Experimento",
            audience: _config["Jwt:Audience"] ?? "Experimento",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

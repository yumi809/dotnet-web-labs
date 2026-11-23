using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Auth.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Api.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "bookhub-auth";
    public string Audience { get; set; } = "bookhub-api";
    /// <summary>Симетричний ключ ≥ 32 байти; у production — з секретів (user-secrets / Kubernetes Secret).</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed record TokenPair(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

/// <summary>JWT access (15 хв) + refresh (7 днів) з ротацією: при оновленні старий токен відкликається і посилається на новий.</summary>
public sealed class TokenService(
    AuthDbContext db,
    UserManager<ApplicationUser> users,
    IOptions<JwtOptions> options,
    ILogger<TokenService> logger)
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<TokenPair> IssueAsync(ApplicationUser user, string? ip, CancellationToken ct)
    {
        var access = await CreateAccessTokenAsync(user);
        var (refreshRaw, refresh) = CreateRefreshToken(user.Id, ip);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        return new TokenPair(access.Token, access.ExpiresAt, refreshRaw, refresh.ExpiresAt);
    }

    /// <summary>Ротація: валідний refresh → новий pair; старий відкликається. Повторне використання відкликаного → відкликаємо всі токени користувача.</summary>
    public async Task<TokenPair?> RotateAsync(string refreshToken, string? ip, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var existing = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return null;

        if (!existing.IsActive)
        {
            if (existing.RevokedAt is not null && existing.ReplacedByTokenHash is not null)
            {
                logger.LogWarning("Reuse of rotated refresh token detected for user {UserId} — revoking all sessions", existing.UserId);
                await RevokeAllAsync(existing.UserId, "token reuse detected", ct);
            }
            return null;
        }

        var access = await CreateAccessTokenAsync(existing.User);
        var (newRaw, replacement) = CreateRefreshToken(existing.UserId, ip);
        existing.RevokedAt = DateTime.UtcNow;
        existing.RevokedReason = "rotated";
        existing.ReplacedByTokenHash = replacement.TokenHash;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        return new TokenPair(access.Token, access.ExpiresAt, newRaw, replacement.ExpiresAt);
    }

    public async Task<bool> RevokeAsync(string refreshToken, string reason, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || !token.IsActive) return false;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedReason = reason;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RevokeAllAsync(string userId, string reason, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) { t.RevokedAt = DateTime.UtcNow; t.RevokedReason = reason; }
        await db.SaveChangesAsync(ct);
    }

    private async Task<(string Token, DateTime ExpiresAt)> CreateAccessTokenAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new("username", user.UserName!),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.FullName)
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var expires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private (string Raw, RefreshToken Entity) CreateRefreshToken(string userId, string? ip)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (raw, new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedByIp = ip
        });
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Identity;

namespace MindLift.Infrastructure.Auth;

/// <summary>Creates JWT access tokens and random refresh tokens (plan 4.3).</summary>
public sealed class TokenService(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(AppUser user, bool isPlatformAdmin)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("tid", user.TenantId.ToString()),
            new("email", user.Email ?? string.Empty),
            new("name", $"{user.FirstName} {user.LastName}".Trim()),
            new("lang", user.Language),
        };

        if (user.MustChangePassword)
        {
            claims.Add(new Claim("mcp", "true"));
        }

        // The session stamp: when the password changes or the person is signed out everywhere, the stamp changes and older tokens stop working.
        if (!string.IsNullOrEmpty(user.SecurityStamp))
        {
            claims.Add(new Claim("sst", user.SecurityStamp));
        }

        if (isPlatformAdmin)
        {
            claims.Add(new Claim("plat", "true"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>
    /// A read-only support sign-in as the company's owner: it names the support session (imp) and who opened it (imp_by),
    /// lasts only until the session ends, and has no refresh token.
    /// </summary>
    public (string Token, DateTime ExpiresAtUtc) CreateSupportToken(AppUser owner, Guid sessionId, string openedBy, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new("sub", owner.Id.ToString()),
            new("tid", owner.TenantId.ToString()),
            new("email", owner.Email ?? string.Empty),
            new("name", $"{owner.FirstName} {owner.LastName}".Trim()),
            new("lang", owner.Language),
            new("imp", sessionId.ToString()),
            new("imp_by", openedBy),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, clock.UtcNow, expiresAt, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, DateTime ExpiresAtUtc) CreatePortalAccessToken(
        Guid portalUserId,
        Guid tenantId,
        Guid contactId,
        Guid? accountId,
        string email,
        string customerName,
        string language,
        int sessionVersion = 1)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new("sub", portalUserId.ToString()),
            new("tid", tenantId.ToString()),
            new("cid", contactId.ToString()),
            new("email", email),
            new("name", customerName),
            new("kind", "portal"),
            new("lang", language),
            new("ver", sessionVersion.ToString()),
            new("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (accountId.HasValue)
        {
            claims.Add(new Claim("aid", accountId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: "mindlift-portal",
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>A new random refresh token, URL-safe base64, 48 bytes of entropy.</summary>
    public static string NewRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 of a token. Only hashes are stored (plan 4.3, 5.2).</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public DateTime RefreshExpiresAt() => clock.UtcNow.AddDays(_options.RefreshTokenDays);
}

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

    public (string Token, DateTime ExpiresAtUtc) CreatePortalAccessToken(
        Guid portalUserId,
        Guid tenantId,
        Guid contactId,
        Guid? accountId,
        string email,
        string customerName,
        string language)
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
            new("lang", language)
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

namespace MindLift.Infrastructure;

/// <summary>Bound from the "Jwt" section. The signing key must come from user-secrets or environment variables outside development.</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "mindlift";
    public string Audience { get; set; } = "mindlift-api";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>Bound from the "Smtp" section. Mailpit listens on localhost:1025 in development.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseStartTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@mindlift.local";
    public string FromName { get; set; } = "MindLift";
}

/// <summary>Bound from the "App" section. Used to build links in emails.</summary>
public sealed class AppOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5082";
}

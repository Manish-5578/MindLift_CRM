using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MindLift.Api.Middleware;
using MindLift.Api.Validation;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Identity;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.UnitTests.Security;

public sealed class UserCreationAndFirstLoginTests
{
    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    [Fact]
    public void TokenService_includes_mcp_claim_when_MustChangePassword_is_true()
    {
        var clock = new FixedClock(DateTime.UtcNow);
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SigningKey = "1234567890123456789012345678901234567890",
            AccessTokenMinutes = 15
        });

        var tokenService = new TokenService(options, clock);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "newuser@example.com",
            FirstName = "First",
            LastName = "Last",
            MustChangePassword = true,
            Language = "en"
        };

        var (token, _) = tokenService.CreateAccessToken(user, isPlatformAdmin: false);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Any(c => c.Type == "mcp" && c.Value == "true").ShouldBeTrue();
    }

    [Fact]
    public void TokenService_omits_mcp_claim_when_MustChangePassword_is_false()
    {
        var clock = new FixedClock(DateTime.UtcNow);
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SigningKey = "1234567890123456789012345678901234567890",
            AccessTokenMinutes = 15
        });

        var tokenService = new TokenService(options, clock);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "activeuser@example.com",
            FirstName = "First",
            LastName = "Last",
            MustChangePassword = false,
            Language = "en"
        };

        var (token, _) = tokenService.CreateAccessToken(user, isPlatformAdmin: false);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Any(c => c.Type == "mcp").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/v1/deals", 403)]
    [InlineData("/api/v1/leads", 403)]
    [InlineData("/api/v1/contacts", 403)]
    [InlineData("/api/v1/users", 403)]
    [InlineData("/api/v1/auth/first-login-change-password", 200)]
    [InlineData("/api/v1/auth/me", 200)]
    [InlineData("/api/v1/app/bootstrap", 200)]
    public async Task MustChangePasswordMiddleware_blocks_general_endpoints_until_reset(string path, int expectedStatus)
    {
        var nextExecuted = false;
        RequestDelegate next = ctx =>
        {
            nextExecuted = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("mcp", "true")
        }, "TestAuth");

        context.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(expectedStatus);
        if (expectedStatus == 200)
        {
            nextExecuted.ShouldBeTrue();
        }
        else
        {
            nextExecuted.ShouldBeFalse();
        }
    }

    [Fact]
    public void CreateUserRequestValidator_validates_required_fields()
    {
        var validator = new CreateUserRequestValidator();
        var valid = new CreateUserRequest("test@example.com", "John", "Doe", Guid.NewGuid(), "Password123!");
        validator.Validate(valid).IsValid.ShouldBeTrue();

        var invalidEmail = new CreateUserRequest("not-an-email", "John", "Doe", Guid.NewGuid(), "Password123!");
        validator.Validate(invalidEmail).IsValid.ShouldBeFalse();

        var shortPassword = new CreateUserRequest("test@example.com", "John", "Doe", Guid.NewGuid(), "short");
        validator.Validate(shortPassword).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void FirstLoginChangePasswordRequestValidator_requires_at_least_8_chars()
    {
        var validator = new FirstLoginChangePasswordRequestValidator();
        validator.Validate(new FirstLoginChangePasswordRequest("ValidPassword123!")).IsValid.ShouldBeTrue();
        validator.Validate(new FirstLoginChangePasswordRequest("short")).IsValid.ShouldBeFalse();
        validator.Validate(new FirstLoginChangePasswordRequest("")).IsValid.ShouldBeFalse();
    }
}

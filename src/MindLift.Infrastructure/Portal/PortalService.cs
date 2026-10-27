using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Communication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Portal;

public sealed class PortalService(
    AppDbContext db,
    ITenantContext tenant,
    TokenService tokenService,
    IBackgroundJobClient jobs,
    ISecretProtector secretProtector,
    IOptions<AppOptions> app,
    IDocumentOcrService ocrService,
    INotificationService notifications,
    IDataScopeService scope) : IPortalService
{
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");
    private static readonly PasswordHasher<PortalUser> Hasher = new();

    /// <summary>A password is checked against this when no usable account exists, so a missing account takes as long to refuse as a wrong password.</summary>
    private static readonly string StandInHash = Hasher.HashPassword(new PortalUser(), Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    private static UnauthorizedException InvalidCredentials() => new("invalid_credentials", "Invalid email or password.");

    /// <summary>The company behind a portal address, or null when there is none or the platform owner switched its customer portal off.</summary>
    private async Task<Tenant?> FindTenantAsync(string tenantSlug, CancellationToken ct)
    {
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct);
        if (t is null)
        {
            return null;
        }

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == t.PlanId, ct);
        return MindLift.Infrastructure.Platform.PlanModules.Disabled(t, plan).Contains(nameof(MindLift.Shared.Enums.AppModule.Portal)) ? null : t;
    }

    public async Task<PortalBrandingDto> GetBrandingAsync(string tenantSlug, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        var tenantSettings = await db.TenantSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ts => ts.TenantId == t.Id && !ts.IsDeleted, ct);

        var settings = await db.PortalSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == t.Id && !s.IsDeleted, ct);

        return new PortalBrandingDto(
            TenantName: !string.IsNullOrWhiteSpace(tenantSettings?.CompanyName) ? tenantSettings.CompanyName : t.Name,
            LogoUrl: settings?.LogoUrl,
            PrimaryColor: tenantSettings?.PrimaryColor ?? "#4F46E5",
            DefaultLanguage: tenantSettings?.DefaultLanguage ?? "en",
            SupportEmail: settings?.SupportEmail ?? tenantSettings?.Email,
            SupportPhone: settings?.SupportPhone ?? tenantSettings?.Phone,
            WelcomeMessage: settings?.WelcomeMessage ?? "Welcome to your customer portal.",
            WelcomeMessageAr: settings?.WelcomeMessageAr,
            Enabled: settings?.IsEnabled ?? true
        );
    }

    public async Task<PortalLoginResponse> LoginAsync(string tenantSlug, PortalLoginRequest req, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        using var scope = tenant.Use(t.Id);
        await EnsurePortalEnabledAsync(t.Id, ct);

        var normEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == t.Id && u.NormalizedEmail == normEmail && !u.IsDeleted, ct);

        // One answer for every reason sign-in can fail (no such address, deactivated, no password yet, locked, wrong password),
        // so the response does not reveal whether an address has portal access.
        var usable = user is { IsActive: true, PasswordHash: { Length: > 0 } } && !(user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow);
        if (!usable)
        {
            Hasher.VerifyHashedPassword(new PortalUser(), StandInHash, req.Password);
            throw InvalidCredentials();
        }

        var verifyResult = Hasher.VerifyHashedPassword(user!, user!.PasswordHash!, req.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
            }
            await db.SaveChangesAsync(ct);
            throw InvalidCredentials();
        }

        user.FailedLoginCount = 0;
        user.LastLoginAt = DateTime.UtcNow;
        return await IssueAsync(t.Id, user, ct);
    }

    public async Task SendMagicLinkAsync(string tenantSlug, PortalMagicLinkRequest req, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct);

        if (t == null) return; // Prevent tenant enumeration

        using var scope = tenant.Use(t.Id);

        var normEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == t.Id && u.NormalizedEmail == normEmail && u.IsActive && !u.IsDeleted, ct);

        if (user == null) return; // Prevent user enumeration

        var plainToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = TokenService.Hash(plainToken);

        var token = new PortalToken
        {
            TenantId = t.Id,
            PortalUserId = user.Id,
            Purpose = "MagicLink",
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        db.PortalTokens.Add(token);
        await db.SaveChangesAsync(ct);

        var magicUrl = $"{app.Value.BaseUrl.TrimEnd('/')}/portal/{t.Slug}/magic?token={plainToken}";
        var subject = $"Your {t.Name} Portal Sign-In Link";
        var body = $"""
            Hello,

            Click the link below to sign in to your {t.Name} Customer Portal:
            {magicUrl}

            This link expires in 15 minutes. If you did not request this, please ignore this email.

            Regards,
            {t.Name} Customer Portal
            """;

        QueueEmail(user.Email, subject, body);
    }

    public async Task<PortalLoginResponse> VerifyMagicLinkAsync(string tenantSlug, PortalMagicLinkVerifyRequest req, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        using var scope = tenant.Use(t.Id);
        await EnsurePortalEnabledAsync(t.Id, ct);

        var hash = TokenService.Hash(req.Token.Trim());
        var token = await db.PortalTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == t.Id && x.TokenHash == hash && x.Purpose == "MagicLink" && !x.IsDeleted, ct)
            ?? throw new UnauthorizedException("invalid_token", "Magic link is invalid or expired.");

        if (token.UsedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("expired_token", "Magic link has expired or already been used.");

        // Claiming the token is a single database step, so two requests with the same link cannot both sign in.
        if (!await TryConsumeAsync(token, ct))
            throw new UnauthorizedException("expired_token", "Magic link has expired or already been used.");

        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == token.PortalUserId && u.TenantId == t.Id && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", token.PortalUserId);

        user.LastLoginAt = DateTime.UtcNow;
        if (!user.IsActive)
        {
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("account_inactive", "Your portal account is deactivated.");
        }

        return await IssueAsync(t.Id, user, ct);
    }

    public async Task<PortalLoginResponse> AcceptInviteAsync(string tenantSlug, AcceptPortalInviteRequest req, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        using var scope = tenant.Use(t.Id);
        await EnsurePortalEnabledAsync(t.Id, ct);
        ValidatePassword(req.Password);

        var hash = TokenService.Hash(req.Token.Trim());
        var token = await db.PortalTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == t.Id && x.TokenHash == hash && x.Purpose == "Invite" && !x.IsDeleted, ct)
            ?? throw new UnauthorizedException("invalid_token", "Invitation token is invalid or expired.");

        if (token.UsedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("expired_token", "Invitation token has expired.");

        if (!await TryConsumeAsync(token, ct))
            throw new UnauthorizedException("expired_token", "Invitation token has expired.");

        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == token.PortalUserId && u.TenantId == t.Id && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", token.PortalUserId);

        // Sending an invitation switches access on. A person deactivated since then stays deactivated.
        if (!user.IsActive)
            throw new UnauthorizedException("account_inactive", "Your portal account is deactivated.");

        user.PasswordHash = Hasher.HashPassword(user, req.Password);
        user.ActivatedAt = DateTime.UtcNow;
        user.LastLoginAt = DateTime.UtcNow;

        return await IssueAsync(t.Id, user, ct);
    }

    public async Task<PortalLoginResponse> RefreshAsync(string tenantSlug, string refreshToken, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct)
            ?? throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");

        using var scope = tenant.Use(t.Id);
        await EnsurePortalEnabledAsync(t.Id, ct);

        var hash = TokenService.Hash(refreshToken.Trim());
        var token = await db.PortalTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == t.Id && x.TokenHash == hash && x.Purpose == RefreshPurpose && !x.IsDeleted, ct)
            ?? throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");

        if (token.UsedAt.HasValue)
        {
            // A rotated token came back: someone else may hold a copy. End every session for this customer.
            await RevokeRefreshTokensAsync(t.Id, token.PortalUserId, ct);
            throw new UnauthorizedException("refresh_token_reused", "Your session has ended. Please sign in again.");
        }

        if (token.ExpiresAt < DateTime.UtcNow)
        {
            throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");
        }

        var user = await db.PortalUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == token.PortalUserId && u.TenantId == t.Id && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("account_inactive", "Your portal account is deactivated.");
        }

        if (!await TryConsumeAsync(token, ct))
        {
            // Another request used this token between the check above and now, which is the same as a reused token.
            await RevokeRefreshTokensAsync(t.Id, token.PortalUserId, ct);
            throw new UnauthorizedException("refresh_token_reused", "Your session has ended. Please sign in again.");
        }

        return await IssueAsync(t.Id, user, ct);
    }

    public async Task LogoutAsync(string tenantSlug, string refreshToken, CancellationToken ct = default)
    {
        var t = await FindTenantAsync(tenantSlug, ct);
        if (t is null) return;

        using var scope = tenant.Use(t.Id);
        var hash = TokenService.Hash(refreshToken.Trim());
        var token = await db.PortalTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == t.Id && x.TokenHash == hash && x.Purpose == RefreshPurpose && !x.IsDeleted, ct);
        if (token is { UsedAt: null })
        {
            token.UsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<PortalProfileDto> GetProfileAsync(Guid portalUserId, CancellationToken ct = default)
    {
        var user = await db.PortalUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == portalUserId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("PortalUser", portalUserId);
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == user.ContactId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", user.ContactId);
        var accountName = user.AccountId.HasValue
            ? await db.Accounts.AsNoTracking().Where(a => a.Id == user.AccountId.Value).Select(a => a.Name).FirstOrDefaultAsync(ct)
            : null;

        return new PortalProfileDto(contact.FirstName, contact.LastName, user.Email, contact.Phone, accountName, user.Language, user.LastLoginAt);
    }

    public async Task<PortalProfileDto> UpdateProfileAsync(Guid portalUserId, UpdatePortalProfileRequest req, CancellationToken ct = default)
    {
        if (req.Language is not ("en" or "ar"))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["language"] = ["Language must be en or ar."] });
        }

        var user = await db.PortalUsers.FirstOrDefaultAsync(u => u.Id == portalUserId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("PortalUser", portalUserId);
        user.Language = req.Language;
        await db.SaveChangesAsync(ct);
        return await GetProfileAsync(portalUserId, ct);
    }

    public async Task ChangePasswordAsync(Guid portalUserId, ChangePortalPasswordRequest req, CancellationToken ct = default)
    {
        ValidatePassword(req.NewPassword);
        var user = await db.PortalUsers.FirstOrDefaultAsync(u => u.Id == portalUserId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("PortalUser", portalUserId);

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["currentPassword"] = ["Too many attempts. Try again later."] });
        }

        var currentIsRight = !string.IsNullOrEmpty(user.PasswordHash)
            && Hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) != PasswordVerificationResult.Failed;
        if (!currentIsRight)
        {
            // Wrong attempts count like wrong sign-ins, so a stolen session cannot be used to guess the password.
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
            }

            await db.SaveChangesAsync(ct);
            throw new ValidationException(new Dictionary<string, string[]> { ["currentPassword"] = ["The current password is not correct."] });
        }

        user.FailedLoginCount = 0;
        user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        await db.SaveChangesAsync(ct);

        // Other devices have to sign in again with the new password.
        await RevokeRefreshTokensAsync(user.TenantId, user.Id, ct);
    }

    private const string RefreshPurpose = "Refresh";
    private const int RefreshDays = 30;
    private const int MinPasswordLength = 8;

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["password"] = [$"Use at least {MinPasswordLength} characters."] });
        }
    }

    /// <summary>Sign-in and refresh are refused while the workspace has switched its portal off.</summary>
    private async Task EnsurePortalEnabledAsync(Guid tenantId, CancellationToken ct)
    {
        var enabled = await db.PortalSettings.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted)
            .Select(s => (bool?)s.IsEnabled)
            .FirstOrDefaultAsync(ct) ?? true;
        if (!enabled)
        {
            throw new ForbiddenException("The customer portal is switched off for this company.", "portal_disabled");
        }

        // A suspended workspace is switched off entirely. A past-due one keeps its portal readable, like its staff reads.
        var status = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Status).FirstOrDefaultAsync(ct);
        if (MindLift.Infrastructure.Tenancy.TenantAccess.IsSuspended(status))
        {
            throw new ForbiddenException("The customer portal is not available for this company.", "portal_disabled");
        }

        // The platform owner (or the plan) can take the Portal module away; that must stop portal access too.
        var t = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is not null)
        {
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == t.PlanId, ct);
            if (MindLift.Infrastructure.Platform.PlanModules.Disabled(t, plan).Contains(nameof(MindLift.Shared.Enums.AppModule.Portal)))
            {
                throw new ForbiddenException("The customer portal is not available for this company.", "portal_disabled");
            }
        }
    }

    public async Task EnsureCustomerAccessAsync(Guid portalUserId, CancellationToken ct = default)
    {
        await EnsurePortalEnabledAsync(CurrentTenantId, ct);
        var active = await db.PortalUsers.AsNoTracking().AnyAsync(u => u.Id == portalUserId && u.IsActive && !u.IsDeleted, ct);
        if (!active)
        {
            throw new UnauthorizedException("portal_session_revoked", "Your portal access has ended. Please sign in again.");
        }
    }

    /// <summary>Marks a token used in one database step. False means someone else used it first.</summary>
    private async Task<bool> TryConsumeAsync(PortalToken token, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await db.PortalTokens.IgnoreQueryFilters()
            .Where(x => x.Id == token.Id && x.TenantId == token.TenantId && x.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.UsedAt, now), ct);
        return rows == 1;
    }

    /// <summary>Queues an email with a live link in it. Sending happens in the background and is retried, and the body is protected while it waits.</summary>
    private void QueueEmail(string to, string subject, string body)
    {
        var protectedBody = secretProtector.Protect(body);
        jobs.Enqueue<PortalEmailJob>(job => job.SendAsync(to, subject, protectedBody, CancellationToken.None));
    }

    private async Task RevokeRefreshTokensAsync(Guid tenantId, Guid portalUserId, CancellationToken ct)
    {
        var live = await db.PortalTokens.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.PortalUserId == portalUserId && x.Purpose == RefreshPurpose && x.UsedAt == null && !x.IsDeleted)
            .ToListAsync(ct);
        foreach (var item in live)
        {
            item.UsedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Saves pending changes, then returns a new access token and a new single-use refresh token.</summary>
    private async Task<PortalLoginResponse> IssueAsync(Guid tenantId, PortalUser user, CancellationToken ct)
    {
        var contact = await db.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == user.ContactId && c.TenantId == tenantId && !c.IsDeleted, ct)
            ?? throw new UnauthorizedException("account_inactive", "Your portal account is no longer linked to a contact.");

        var account = user.AccountId.HasValue
            ? await db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == user.AccountId.Value && a.TenantId == tenantId && !a.IsDeleted, ct)
            : null;

        var refreshPlain = TokenService.NewRefreshToken();
        db.PortalTokens.Add(new PortalToken
        {
            TenantId = tenantId,
            PortalUserId = user.Id,
            Purpose = RefreshPurpose,
            TokenHash = TokenService.Hash(refreshPlain),
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshDays)
        });
        await db.SaveChangesAsync(ct);

        var customerName = $"{contact.FirstName} {contact.LastName}".Trim();
        var (accessToken, expiresAt) = tokenService.CreatePortalAccessToken(
            user.Id, tenantId, contact.Id, account?.Id, user.Email, customerName, user.Language);

        return new PortalLoginResponse(
            AccessToken: accessToken,
            PortalUserId: user.Id,
            ContactId: contact.Id,
            AccountId: account?.Id,
            CustomerName: customerName,
            AccountName: account?.Name,
            Language: user.Language,
            ExpiresAtUtc: expiresAt,
            RefreshToken: refreshPlain);
    }

    public async Task<PortalHomeDto> GetHomeAsync(Guid portalUserId, Guid contactId, Guid? accountId, CancellationToken ct = default)
    {
        var settings = await db.PortalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted, ct);

        var contact = await db.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contactId && !c.IsDeleted, ct);

        var account = accountId.HasValue
            ? await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId.Value && !a.IsDeleted, ct)
            : null;

        var customerName = contact != null ? $"{contact.FirstName} {contact.LastName}".Trim() : "Valued Customer";

        // Open Document Requests
        var requests = await db.DocumentRequests
            .AsNoTracking()
            .Where(r => r.ContactId == contactId && r.Status == "Open" && !r.IsDeleted)
            .OrderBy(r => r.DueDate)
            .Select(r => new PortalDocumentRequestDto(
                r.Id,
                r.ContactId,
                r.AccountId,
                r.DealId,
                r.Title,
                r.Description,
                r.DueDate,
                r.Status,
                r.CreatedAt))
            .ToListAsync(ct);

        // Recent Shared Documents
        var docQuery = db.BusinessDocuments
            .AsNoTracking()
            .Where(d => d.SharedWithPortal && !d.IsDeleted &&
                        (d.AssociatedContactId == contactId || (accountId != null && d.AssociatedAccountId == accountId)));

        var recentDocs = await docQuery
            .OrderByDescending(d => d.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        var mappedRecentDocs = recentDocs.Select(d => new BusinessDocumentDto
        {
            Id = d.Id,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            Category = d.Category,
            AssociatedAccountId = d.AssociatedAccountId,
            AssociatedDealId = d.AssociatedDealId,
            AssociatedQuoteId = d.AssociatedQuoteId,
            AssociatedInvoiceId = d.AssociatedInvoiceId,
            OcrStatus = (DocumentOcrStatus)d.OcrStatus,
            UploadedAt = d.CreatedAt,
            OcrResult = d.OcrStatus == (int)DocumentOcrStatus.Completed ? new OcrResultDto
            {
                DocumentId = d.Id,
                Status = DocumentOcrStatus.Completed,
                DocumentType = d.Category,
                ExtractedText = d.OcrRawText,
                DetectedVendor = d.DetectedVendor,
                DetectedInvoiceNumber = d.DetectedInvoiceNumber,
                DetectedTotal = d.DetectedTotal,
                DetectedDate = d.DetectedDate
            } : null
        }).ToList();

        // Key Info Cards
        var keyInfo = new List<PortalKeyInfoDto>();
        var readyDocs = await docQuery.Where(d => d.Status == "Ready").ToListAsync(ct);
        foreach (var group in readyDocs.GroupBy(d => d.Category))
        {
            var latest = group.OrderByDescending(d => d.PeriodDate ?? d.IssueDate ?? DateOnly.FromDateTime(d.CreatedAt)).First();
            var daysLeft = latest.ExpiryDate.HasValue ? (int)(latest.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow.Date).TotalDays : (int?)null;
            var validity = daysLeft switch
            {
                < 0 => "expired",
                <= 30 => "expiring",
                _ => "valid"
            };

            var fields = new List<PortalKeyInfoFieldDto>();
            if (!string.IsNullOrWhiteSpace(latest.DetectedVendor))
                fields.Add(new PortalKeyInfoFieldDto("Vendor", latest.DetectedVendor, "Text", false));
            if (!string.IsNullOrWhiteSpace(latest.DetectedInvoiceNumber))
                fields.Add(new PortalKeyInfoFieldDto("Doc #", latest.DetectedInvoiceNumber, "Id", false));
            if (latest.DetectedTotal.HasValue)
                fields.Add(new PortalKeyInfoFieldDto("Total", latest.DetectedTotal.Value.ToString("C"), "Currency", false));

            keyInfo.Add(new PortalKeyInfoDto(
                TypeKey: group.Key.ToLowerInvariant(),
                TypeName: group.Key,
                DocumentId: latest.Id,
                ExpiryDate: latest.ExpiryDate,
                DaysLeft: daysLeft,
                Validity: validity,
                Fields: fields
            ));
        }

        // Metrics (if enabled): totals of the customer's ready documents by year of the document date.
        var metrics = new List<PortalMetricCardDto>();
        if (settings?.ShowMetrics ?? true)
        {
            var byYear = readyDocs
                .Where(d => d.DetectedTotal.HasValue)
                .GroupBy(d => (d.DetectedDate ?? d.CreatedAt).Year)
                .OrderBy(g => g.Key)
                .Select(g => new PortalMetricPointDto(g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Sum(d => d.DetectedTotal!.Value)))
                .ToList();

            if (byYear.Count > 0)
            {
                var latest = byYear[^1].Value;
                decimal? previous = byYear.Count > 1 ? byYear[^2].Value : null;
                decimal? change = previous is > 0 ? Math.Round((latest - previous.Value) / previous.Value * 100m, 1) : null;
                var currency = await db.TenantSettings.AsNoTracking().Select(s => s.BaseCurrency).FirstOrDefaultAsync(ct) ?? "USD";

                metrics.Add(new PortalMetricCardDto(
                    Key: "documented_totals",
                    Label: "Document totals",
                    Currency: currency,
                    Latest: latest,
                    Previous: previous,
                    ChangePct: change,
                    Series: byYear));
            }
        }

        return new PortalHomeDto(
            CustomerName: customerName,
            AccountName: account?.Name,
            WelcomeMessage: settings?.WelcomeMessage ?? "Welcome to your customer portal.",
            OpenRequests: requests,
            KeyInfo: keyInfo,
            Metrics: metrics,
            RecentDocuments: mappedRecentDocs
        );
    }

    public async Task<IReadOnlyList<BusinessDocumentDto>> GetCustomerDocumentsAsync(
        Guid contactId,
        Guid? accountId,
        string? category,
        string? search,
        CancellationToken ct = default)
    {
        var q = db.BusinessDocuments
            .AsNoTracking()
            .Where(d => d.SharedWithPortal && !d.IsDeleted &&
                        (d.AssociatedContactId == contactId || (accountId != null && d.AssociatedAccountId == accountId)));

        if (!string.IsNullOrWhiteSpace(category))
        {
            q = q.Where(d => d.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(d => d.FileName.Contains(search) || (d.DetectedVendor != null && d.DetectedVendor.Contains(search)));
        }

        var list = await q.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        return list.Select(d => new BusinessDocumentDto
        {
            Id = d.Id,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            Category = d.Category,
            AssociatedAccountId = d.AssociatedAccountId,
            AssociatedDealId = d.AssociatedDealId,
            AssociatedQuoteId = d.AssociatedQuoteId,
            AssociatedInvoiceId = d.AssociatedInvoiceId,
            OcrStatus = (DocumentOcrStatus)d.OcrStatus,
            UploadedAt = d.CreatedAt,
            OcrResult = d.OcrStatus == (int)DocumentOcrStatus.Completed ? new OcrResultDto
            {
                DocumentId = d.Id,
                Status = DocumentOcrStatus.Completed,
                DocumentType = d.Category,
                ExtractedText = d.OcrRawText,
                DetectedVendor = d.DetectedVendor,
                DetectedInvoiceNumber = d.DetectedInvoiceNumber,
                DetectedTotal = d.DetectedTotal,
                DetectedDate = d.DetectedDate
            } : null
        }).ToList();
    }

    public async Task<IReadOnlyList<PortalDocumentRequestDto>> GetCustomerRequestsAsync(Guid contactId, CancellationToken ct = default)
    {
        var list = await db.DocumentRequests
            .AsNoTracking()
            .Where(r => r.ContactId == contactId && !r.IsDeleted)
            .OrderBy(r => r.DueDate)
            .Select(r => new PortalDocumentRequestDto(
                r.Id,
                r.ContactId,
                r.AccountId,
                r.DealId,
                r.Title,
                r.Description,
                r.DueDate,
                r.Status,
                r.CreatedAt))
            .ToListAsync(ct);

        return list;
    }

    public async Task<BusinessDocumentDto> SubmitDocumentRequestAsync(
        Guid contactId,
        Guid? accountId,
        Guid requestId,
        string fileName,
        string contentType,
        long size,
        byte[] bytes,
        CancellationToken ct = default)
    {
        var req = await db.DocumentRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.ContactId == contactId && !r.IsDeleted, ct)
            ?? throw new NotFoundException("DocumentRequest", requestId);

        if (req.Status is "Completed" or "Cancelled")
        {
            throw new BusinessRuleException("request_closed", "This request is closed and no longer accepts uploads.");
        }

        var doc = await ocrService.UploadDocumentAsync(
            fileName,
            contentType,
            size,
            bytes,
            "Identity",
            req.AccountId ?? accountId,
            req.DealId,
            null,
            null,
            contactId: contactId,
            sharedWithPortal: true,
            ct: ct);

        var entity = await db.BusinessDocuments.FirstAsync(d => d.Id == doc.Id, ct);
        entity.Status = "NeedsReview";

        req.Status = "Submitted";
        await db.SaveChangesAsync(ct);

        // Tell the person who asked and the contact's owner.
        var ownerId = await db.Contacts.AsNoTracking().Where(c => c.Id == contactId).Select(c => (Guid?)c.OwnerId).FirstOrDefaultAsync(ct);
        var recipients = new[] { req.CreatedById, ownerId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (recipients.Count > 0)
        {
            await notifications.NotifyAsync(new NotificationRequest(
                recipients,
                NotificationTypes.PortalUpload,
                $"Customer uploaded \"{req.Title}\"",
                fileName,
                "Contact",
                contactId,
                "/documents"), ct);
        }

        doc.Status = "NeedsReview";
        return doc;
    }

    public async Task<PortalSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await db.PortalSettings.FirstOrDefaultAsync(x => !x.IsDeleted, ct);
        if (s == null)
        {
            s = new PortalSettings { TenantId = CurrentTenantId };
            db.PortalSettings.Add(s);
            await db.SaveChangesAsync(ct);
        }

        return new PortalSettingsDto(
            s.IsEnabled,
            s.WelcomeMessage,
            s.WelcomeMessageAr,
            s.ShowMetrics,
            s.AllowGeneralUploads,
            s.NotifyCustomerOnExpiry,
            s.SupportEmail,
            s.SupportPhone
        );
    }

    public async Task<PortalSettingsDto> SaveSettingsAsync(SavePortalSettingsRequest req, CancellationToken ct = default)
    {
        var s = await db.PortalSettings.FirstOrDefaultAsync(x => !x.IsDeleted, ct);
        if (s == null)
        {
            s = new PortalSettings { TenantId = CurrentTenantId };
            db.PortalSettings.Add(s);
        }

        s.IsEnabled = req.IsEnabled;
        s.WelcomeMessage = req.WelcomeMessage;
        s.WelcomeMessageAr = req.WelcomeMessageAr;
        s.ShowMetrics = req.ShowMetrics;
        s.AllowGeneralUploads = req.AllowGeneralUploads;
        s.NotifyCustomerOnExpiry = req.NotifyCustomerOnExpiry;
        s.SupportEmail = req.SupportEmail;
        s.SupportPhone = req.SupportPhone;

        await db.SaveChangesAsync(ct);
        return new PortalSettingsDto(s.IsEnabled, s.WelcomeMessage, s.WelcomeMessageAr, s.ShowMetrics, s.AllowGeneralUploads, s.NotifyCustomerOnExpiry, s.SupportEmail, s.SupportPhone);
    }

    public async Task InviteContactAsync(InvitePortalUserRequest req, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == req.ContactId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", req.ContactId);
        await EnsureInScopeAsync(AppModule.Contacts, contact.OwnerId, "Contact", contact.Id, ct);

        if (string.IsNullOrWhiteSpace(req.Email))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["email"] = ["An email address is required."] });
        }

        var normEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.PortalUsers.FirstOrDefaultAsync(u => u.NormalizedEmail == normEmail && !u.IsDeleted, ct);

        if (user != null && user.ContactId != contact.Id)
        {
            throw new ConflictException("portal_email_taken", "This email already has portal access for another contact.");
        }

        if (user == null)
        {
            user = new PortalUser
            {
                TenantId = CurrentTenantId,
                ContactId = contact.Id,
                AccountId = contact.AccountId,
                Email = req.Email.Trim(),
                NormalizedEmail = normEmail,
                IsActive = true,
                InvitedAt = DateTime.UtcNow
            };
            db.PortalUsers.Add(user);
        }
        else
        {
            user.InvitedAt = DateTime.UtcNow;
            user.IsActive = true;
        }

        var plainToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = TokenService.Hash(plainToken);

        var token = new PortalToken
        {
            TenantId = CurrentTenantId,
            PortalUserId = user.Id,
            Purpose = "Invite",
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        db.PortalTokens.Add(token);
        await db.SaveChangesAsync(ct);

        var tenantEntity = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == CurrentTenantId, ct);
        var inviteUrl = $"{app.Value.BaseUrl.TrimEnd('/')}/portal/{tenantEntity.Slug}/accept-invite?token={plainToken}";

        QueueEmail(
            user.Email,
            $"{tenantEntity.Name} invited you to their Customer Portal",
            $"You have been invited to the customer portal. Set up your access here: {inviteUrl}");
    }

    public async Task<PortalAccessStatusDto> GetContactAccessAsync(Guid contactId, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", contactId);
        await EnsureInScopeAsync(AppModule.Contacts, contact.OwnerId, "Contact", contact.Id, ct);

        var user = await db.PortalUsers.AsNoTracking()
            .Where(u => u.ContactId == contactId && !u.IsDeleted)
            .OrderByDescending(u => u.InvitedAt)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return new PortalAccessStatusDto(contactId, "NotInvited", null, null, null, null);
        }

        var status = !user.IsActive ? "Disabled" : user.ActivatedAt.HasValue ? "Active" : "Invited";
        return new PortalAccessStatusDto(contactId, status, user.Email, user.InvitedAt, user.ActivatedAt, user.LastLoginAt);
    }

    public async Task<PortalDocumentRequestDto> UpdateDocumentRequestStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        if (status is not ("Completed" or "Cancelled" or "Open"))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["status"] = ["Status must be Open, Completed or Cancelled."] });
        }

        var entity = await db.DocumentRequests.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
            ?? throw new NotFoundException("DocumentRequest", id);
        var ownerId = await db.Contacts.Where(c => c.Id == entity.ContactId).Select(c => (Guid?)c.OwnerId).FirstOrDefaultAsync(ct);
        if (ownerId is { } oid) await EnsureInScopeAsync(AppModule.Contacts, oid, "DocumentRequest", id, ct);

        entity.Status = status;

        // Accepting a submission marks the customer's uploaded files for this request as reviewed.
        if (status == "Completed")
        {
            var pending = await db.BusinessDocuments
                .Where(d => d.AssociatedContactId == entity.ContactId && d.Status == "NeedsReview" && !d.IsDeleted)
                .ToListAsync(ct);
            foreach (var d in pending)
            {
                d.Status = "Ready";
                d.ReviewedAt = DateTime.UtcNow;
                d.ReviewedById = tenant.UserId;
            }
        }

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task SetDocumentSharingAsync(Guid documentId, bool shared, CancellationToken ct = default)
    {
        var doc = await db.BusinessDocuments.FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Document", documentId);

        if (shared && doc.AssociatedContactId is null && doc.AssociatedAccountId is null)
        {
            throw new BusinessRuleException("document_unlinked", "Link the document to a contact or account before sharing it with the portal.");
        }

        doc.SharedWithPortal = shared;
        await db.SaveChangesAsync(ct);
    }

    private static PortalDocumentRequestDto ToDto(DocumentRequest r) =>
        new(r.Id, r.ContactId, r.AccountId, r.DealId, r.Title, r.Description, r.DueDate, r.Status, r.CreatedAt);

    /// <summary>404s when a CRM record is outside the staff caller's row scope for its module.</summary>
    private async Task EnsureInScopeAsync(AppModule module, Guid ownerId, string entity, Guid id, CancellationToken ct)
    {
        var ids = await scope.GetScopeUserIdsAsync(module, ct);
        if (ids is not null && !ids.Contains(ownerId))
        {
            throw new NotFoundException(entity, id);
        }
    }

    public async Task<IReadOnlyList<PortalDocumentRequestDto>> GetDocumentRequestsForRecordAsync(
        Guid? contactId,
        Guid? accountId,
        Guid? dealId,
        CancellationToken ct = default)
    {
        var q = db.DocumentRequests.AsNoTracking().Where(r => !r.IsDeleted);
        if (contactId.HasValue) q = q.Where(r => r.ContactId == contactId.Value);
        if (accountId.HasValue) q = q.Where(r => r.AccountId == accountId.Value);
        if (dealId.HasValue) q = q.Where(r => r.DealId == dealId.Value);
        var contactScope = await scope.GetScopeUserIdsAsync(AppModule.Contacts, ct);
        if (contactScope is not null)
        {
            q = q.Where(r => db.Contacts.Any(c => c.Id == r.ContactId && contactScope.Contains(c.OwnerId)));
        }

        return await q.OrderByDescending(r => r.CreatedAt).Select(r => new PortalDocumentRequestDto(
            r.Id,
            r.ContactId,
            r.AccountId,
            r.DealId,
            r.Title,
            r.Description,
            r.DueDate,
            r.Status,
            r.CreatedAt
        )).ToListAsync(ct);
    }

    public async Task<PortalDocumentRequestDto> CreateDocumentRequestAsync(SaveDocumentRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["title"] = ["A title is required."] });
        }

        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.ContactId && !c.IsDeleted, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["contactId"] = ["Choose an existing contact."] });
        await EnsureInScopeAsync(AppModule.Contacts, contact.OwnerId, "Contact", contact.Id, ct);

        // The account and deal must be this company's own, belong to this contact and be visible to the caller.
        // Otherwise a customer's upload could land on an unrelated account or deal.
        if (req.AccountId is { } accountId)
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, ct);
            if (account is null || contact.AccountId != accountId)
            {
                throw new ValidationException(new Dictionary<string, string[]> { ["accountId"] = ["Choose the contact's own account."] });
            }
            await EnsureInScopeAsync(AppModule.Accounts, account.OwnerId, "Account", account.Id, ct);
        }

        var effectiveAccountId = req.AccountId ?? contact.AccountId;
        if (req.DealId is { } dealId)
        {
            var deal = await db.Deals.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealId && !d.IsDeleted, ct);
            var related = deal is not null
                && ((effectiveAccountId is { } accId && deal.AccountId == accId)
                    || await db.DealContacts.AnyAsync(dc => dc.DealId == dealId && dc.ContactId == contact.Id, ct));
            if (!related)
            {
                throw new ValidationException(new Dictionary<string, string[]> { ["dealId"] = ["Choose a deal linked to this contact or its account."] });
            }
            await EnsureInScopeAsync(AppModule.Deals, deal!.OwnerId, "Deal", deal.Id, ct);
        }

        var entity = new DocumentRequest
        {
            TenantId = CurrentTenantId,
            ContactId = contact.Id,
            AccountId = effectiveAccountId,
            DealId = req.DealId,
            Title = req.Title.Trim(),
            Description = req.Description,
            DueDate = req.DueDate,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };

        db.DocumentRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        return ToDto(entity);
    }
}

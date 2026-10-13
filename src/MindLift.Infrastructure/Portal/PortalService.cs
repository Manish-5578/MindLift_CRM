using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    IEmailSender emailSender,
    IDocumentOcrService ocrService) : IPortalService
{
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");
    private static readonly PasswordHasher<PortalUser> Hasher = new();

    public async Task<PortalBrandingDto> GetBrandingAsync(string tenantSlug, CancellationToken ct = default)
    {
        var t = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct)
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
        var t = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        using var scope = tenant.Use(t.Id);

        var normEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == t.Id && u.NormalizedEmail == normEmail && !u.IsDeleted, ct)
            ?? throw new UnauthorizedException("invalid_credentials", "Invalid email or password.");

        if (!user.IsActive)
            throw new UnauthorizedException("account_inactive", "Your portal account is deactivated.");

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            throw new UnauthorizedException("account_locked", "Account is temporarily locked. Try again later.");

        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedException("password_not_set", "Password not set. Please use magic link sign-in.");

        var verifyResult = Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
            }
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("invalid_credentials", "Invalid email or password.");
        }

        user.FailedLoginCount = 0;
        user.LastLoginAt = DateTime.UtcNow;

        var contact = await db.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == user.ContactId && c.TenantId == t.Id && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", user.ContactId);

        var account = user.AccountId.HasValue
            ? await db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == user.AccountId.Value && !a.IsDeleted, ct)
            : null;

        await db.SaveChangesAsync(ct);

        var customerName = $"{contact.FirstName} {contact.LastName}".Trim();
        var (accessToken, _) = tokenService.CreatePortalAccessToken(
            user.Id,
            t.Id,
            contact.Id,
            account?.Id,
            user.Email,
            customerName,
            user.Language);

        return new PortalLoginResponse(
            AccessToken: accessToken,
            PortalUserId: user.Id,
            ContactId: contact.Id,
            AccountId: account?.Id,
            CustomerName: customerName,
            AccountName: account?.Name,
            Language: user.Language
        );
    }

    public async Task SendMagicLinkAsync(string tenantSlug, PortalMagicLinkRequest req, CancellationToken ct = default)
    {
        var t = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct);

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

        var magicUrl = $"/portal/{t.Slug}/magic?token={plainToken}";
        var subject = $"Your {t.Name} Portal Sign-In Link";
        var body = $"""
            Hello,

            Click the link below to sign in to your {t.Name} Customer Portal:
            {magicUrl}

            This link expires in 15 minutes. If you did not request this, please ignore this email.

            Regards,
            {t.Name} Customer Portal
            """;

        try
        {
            await emailSender.SendAsync(user.Email, subject, body, ct);
        }
        catch { }
    }

    public async Task<PortalLoginResponse> VerifyMagicLinkAsync(string tenantSlug, PortalMagicLinkVerifyRequest req, CancellationToken ct = default)
    {
        var t = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        using var scope = tenant.Use(t.Id);

        var hash = TokenService.Hash(req.Token.Trim());
        var token = await db.PortalTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == t.Id && x.TokenHash == hash && x.Purpose == "MagicLink" && !x.IsDeleted, ct)
            ?? throw new UnauthorizedException("invalid_token", "Magic link is invalid or expired.");

        if (token.UsedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("expired_token", "Magic link has expired or already been used.");

        token.UsedAt = DateTime.UtcNow;

        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == token.PortalUserId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", token.PortalUserId);

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var contact = await db.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == user.ContactId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", user.ContactId);

        var account = user.AccountId.HasValue
            ? await db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == user.AccountId.Value && !a.IsDeleted, ct)
            : null;

        var customerName = $"{contact.FirstName} {contact.LastName}".Trim();
        var (accessToken, _) = tokenService.CreatePortalAccessToken(
            user.Id,
            t.Id,
            contact.Id,
            account?.Id,
            user.Email,
            customerName,
            user.Language);

        return new PortalLoginResponse(
            AccessToken: accessToken,
            PortalUserId: user.Id,
            ContactId: contact.Id,
            AccountId: account?.Id,
            CustomerName: customerName,
            AccountName: account?.Name,
            Language: user.Language
        );
    }

    public async Task<PortalLoginResponse> AcceptInviteAsync(string tenantSlug, AcceptPortalInviteRequest req, CancellationToken ct = default)
    {
        var t = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Slug == tenantSlug, ct)
            ?? throw new NotFoundException("Tenant", tenantSlug);

        using var scope = tenant.Use(t.Id);

        var hash = TokenService.Hash(req.Token.Trim());
        var token = await db.PortalTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == t.Id && x.TokenHash == hash && x.Purpose == "Invite" && !x.IsDeleted, ct)
            ?? throw new UnauthorizedException("invalid_token", "Invitation token is invalid or expired.");

        if (token.UsedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedException("expired_token", "Invitation token has expired.");

        token.UsedAt = DateTime.UtcNow;

        var user = await db.PortalUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == token.PortalUserId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", token.PortalUserId);

        user.PasswordHash = Hasher.HashPassword(user, req.Password);
        user.ActivatedAt = DateTime.UtcNow;
        user.LastLoginAt = DateTime.UtcNow;
        user.IsActive = true;

        await db.SaveChangesAsync(ct);

        var contact = await db.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == user.ContactId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", user.ContactId);

        var account = user.AccountId.HasValue
            ? await db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == user.AccountId.Value && !a.IsDeleted, ct)
            : null;

        var customerName = $"{contact.FirstName} {contact.LastName}".Trim();
        var (accessToken, _) = tokenService.CreatePortalAccessToken(
            user.Id,
            t.Id,
            contact.Id,
            account?.Id,
            user.Email,
            customerName,
            user.Language);

        return new PortalLoginResponse(
            AccessToken: accessToken,
            PortalUserId: user.Id,
            ContactId: contact.Id,
            AccountId: account?.Id,
            CustomerName: customerName,
            AccountName: account?.Name,
            Language: user.Language
        );
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

        // Metrics (if enabled)
        var metrics = new List<PortalMetricCardDto>();
        if (settings?.ShowMetrics ?? true)
        {
            var invoiceTotals = readyDocs.Where(d => d.DetectedTotal.HasValue).Select(d => d.DetectedTotal!.Value).ToList();
            if (invoiceTotals.Count > 0)
            {
                metrics.Add(new PortalMetricCardDto(
                    Key: "total_invoiced",
                    Label: "Total Invoiced",
                    Currency: "USD",
                    Latest: invoiceTotals.Sum(),
                    Previous: invoiceTotals.Count > 1 ? invoiceTotals.Take(invoiceTotals.Count - 1).Sum() : null,
                    ChangePct: 15.2m,
                    Series:
                    [
                        new PortalMetricPointDto("2024", invoiceTotals.Sum() * 0.7m),
                        new PortalMetricPointDto("2025", invoiceTotals.Sum() * 0.9m),
                        new PortalMetricPointDto("2026", invoiceTotals.Sum())
                    ]
                ));
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
        entity.Status = "Ready";

        req.Status = "Submitted";
        await db.SaveChangesAsync(ct);

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

    public async Task<string> InviteContactAsync(InvitePortalUserRequest req, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == req.ContactId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Contact", req.ContactId);

        var normEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.PortalUsers.FirstOrDefaultAsync(u => u.NormalizedEmail == normEmail && !u.IsDeleted, ct);

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
        var inviteUrl = $"/portal/{tenantEntity.Slug}/accept-invite?token={plainToken}";

        try
        {
            await emailSender.SendAsync(
                user.Email,
                $"{tenantEntity.Name} invited you to their Customer Portal",
                $"You have been invited to the customer portal. Set up your access here: {inviteUrl}",
                ct);
        }
        catch { }

        return plainToken;
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
        var entity = new DocumentRequest
        {
            TenantId = CurrentTenantId,
            ContactId = req.ContactId,
            AccountId = req.AccountId,
            DealId = req.DealId,
            Title = req.Title,
            Description = req.Description,
            DueDate = req.DueDate,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };

        db.DocumentRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        return new PortalDocumentRequestDto(
            entity.Id,
            entity.ContactId,
            entity.AccountId,
            entity.DealId,
            entity.Title,
            entity.Description,
            entity.DueDate,
            entity.Status,
            entity.CreatedAt
        );
    }
}

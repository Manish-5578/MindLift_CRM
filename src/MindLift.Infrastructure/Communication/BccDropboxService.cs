using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class BccDropboxService(
    AppDbContext db,
    ITenantContext tenantContext,
    IFeatureFlags featureFlags,
    ITimelineEventBus timeline) : IBccDropboxService
{
    private static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static string NormalizeEmail(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var trimmed = input.Trim();
        var open = trimmed.LastIndexOf('<');
        var close = trimmed.LastIndexOf('>');
        if (open >= 0 && close > open)
        {
            trimmed = trimmed.Substring(open + 1, close - open - 1).Trim();
        }
        return trimmed.ToLowerInvariant();
    }

    private static string ExtractDomain(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email.Substring(at + 1).ToLowerInvariant() : string.Empty;
    }

    public async Task<BccDropboxAddressDto> GetOrCreateUserDropboxAddressAsync(Guid userId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var existing = await db.BccDropboxAddresses
            .FirstOrDefaultAsync(a => a.UserId == userId, ct);

        if (existing != null)
        {
            return new BccDropboxAddressDto(
                existing.Id,
                existing.UserId,
                existing.DropboxToken,
                existing.DropboxEmail,
                existing.IsActive,
                existing.TotalEmailsReceived,
                existing.LastReceivedAtUtc);
        }

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        var token = GenerateToken();
        var email = $"dropbox+{token}@{settings.DropboxDomain}";

        var address = new BccDropboxAddress
        {
            TenantId = tenantId,
            UserId = userId,
            DropboxToken = token,
            DropboxEmail = email,
            IsActive = true,
            TotalEmailsReceived = 0
        };

        db.BccDropboxAddresses.Add(address);
        await db.SaveChangesAsync(ct);

        return new BccDropboxAddressDto(
            address.Id,
            address.UserId,
            address.DropboxToken,
            address.DropboxEmail,
            address.IsActive,
            address.TotalEmailsReceived,
            address.LastReceivedAtUtc);
    }

    public async Task<BccDropboxAddressDto> RegenerateUserDropboxAddressAsync(Guid userId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var address = await db.BccDropboxAddresses
            .FirstOrDefaultAsync(a => a.UserId == userId, ct);

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        var token = GenerateToken();
        var email = $"dropbox+{token}@{settings.DropboxDomain}";

        if (address == null)
        {
            address = new BccDropboxAddress
            {
                TenantId = tenantId,
                UserId = userId,
                DropboxToken = token,
                DropboxEmail = email,
                IsActive = true
            };
            db.BccDropboxAddresses.Add(address);
        }
        else
        {
            address.DropboxToken = token;
            address.DropboxEmail = email;
            address.IsActive = true;
        }

        await db.SaveChangesAsync(ct);

        return new BccDropboxAddressDto(
            address.Id,
            address.UserId,
            address.DropboxToken,
            address.DropboxEmail,
            address.IsActive,
            address.TotalEmailsReceived,
            address.LastReceivedAtUtc);
    }

    public async Task<BccDropboxSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        return new BccDropboxSettingsDto(
            settings.DropboxDomain,
            settings.AutoCreateLead,
            settings.AutoCreateContact,
            settings.MatchDeals,
            settings.DefaultPrivacy,
            settings.ExcludedDomains,
            settings.LogNonMatched);
    }

    public async Task<BccDropboxSettingsDto> UpdateSettingsAsync(UpdateBccDropboxSettingsRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await db.BccDropboxSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (settings == null)
        {
            settings = new BccDropboxSettings { TenantId = tenantId };
            db.BccDropboxSettings.Add(settings);
        }

        if (!string.IsNullOrWhiteSpace(request.DropboxDomain))
            settings.DropboxDomain = request.DropboxDomain.Trim().ToLowerInvariant();

        settings.AutoCreateLead = request.AutoCreateLead;
        settings.AutoCreateContact = request.AutoCreateContact;
        settings.MatchDeals = request.MatchDeals;
        settings.DefaultPrivacy = string.Equals(request.DefaultPrivacy, "Private", StringComparison.OrdinalIgnoreCase) ? "Private" : "Public";
        settings.ExcludedDomains = request.ExcludedDomains?.Select(d => d.Trim().ToLowerInvariant()).Where(d => !string.IsNullOrEmpty(d)).Distinct().ToArray() ?? [];
        settings.LogNonMatched = request.LogNonMatched;

        await db.SaveChangesAsync(ct);

        return new BccDropboxSettingsDto(
            settings.DropboxDomain,
            settings.AutoCreateLead,
            settings.AutoCreateContact,
            settings.MatchDeals,
            settings.DefaultPrivacy,
            settings.ExcludedDomains,
            settings.LogNonMatched);
    }

    public async Task<InboundBccEmailResult> ProcessInboundEmailAsync(InboundBccEmailRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return new InboundBccEmailResult(false, "InvalidToken", null, null, null, null, null, "Dropbox token is required.");

        var cleanToken = request.Token.Trim().ToLowerInvariant();

        // 1. Look up address by token ignoring query filter (public webhook)
        var address = await db.BccDropboxAddresses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.DropboxToken == cleanToken && !a.IsDeleted, ct);

        if (address == null || !address.IsActive)
            return new InboundBccEmailResult(false, "AddressNotFoundOrInactive", null, null, null, null, null, "BCC dropbox address was not found or is inactive.");

        var targetTenantId = address.TenantId;
        using var scope = tenantContext.Use(targetTenantId);

        // 2. Validate feature flag
        var isEnabled = await featureFlags.IsEnabledAsync("bcc-dropbox", ct);
        if (!isEnabled)
            throw new ForbiddenException();

        var settings = await GetSettingsEntityAsync(targetTenantId, ct);

        // 3. Check excluded domains
        var fromNorm = NormalizeEmail(request.From);
        var toNormList = request.To.Select(NormalizeEmail).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
        var allParticipants = new List<string> { fromNorm };
        allParticipants.AddRange(toNormList);

        if (settings.ExcludedDomains != null && settings.ExcludedDomains.Length > 0)
        {
            var isExcluded = allParticipants.Any(email =>
            {
                var d = ExtractDomain(email);
                return settings.ExcludedDomains.Any(ex => string.Equals(ex, d, StringComparison.OrdinalIgnoreCase));
            });

            if (isExcluded)
            {
                var skipLog = new BccDropboxLog
                {
                    TenantId = targetTenantId,
                    BccDropboxAddressId = address.Id,
                    UserId = address.UserId,
                    MessageId = request.MessageId ?? Guid.NewGuid().ToString(),
                    FromAddress = request.From,
                    RecipientAddresses = request.To,
                    Subject = request.Subject ?? string.Empty,
                    ActionTaken = "SkippedExcludedDomain",
                    ReceivedAtUtc = DateTime.UtcNow
                };
                db.BccDropboxLogs.Add(skipLog);
                await db.SaveChangesAsync(ct);

                return new InboundBccEmailResult(true, "SkippedExcludedDomain", null, null, null, null, null, "Email skipped due to domain exclusion rule.");
            }
        }

        // 4. Try matching recipient emails to CRM records (Lead, Contact, Account, Deal)
        Guid? matchedLeadId = null;
        Guid? matchedContactId = null;
        Guid? matchedAccountId = null;
        Guid? matchedDealId = null;
        string actionTaken = "MatchedExisting";

        foreach (var recipient in toNormList)
        {
            // Try lead
            var lead = await db.Set<Lead>()
                .FirstOrDefaultAsync(l => l.Email != null && l.Email.ToLower() == recipient && !l.IsDeleted, ct);

            if (lead != null)
            {
                matchedLeadId = lead.Id;
                break;
            }

            // Try contact
            var contact = await db.Set<Contact>()
                .FirstOrDefaultAsync(c => c.Email != null && c.Email.ToLower() == recipient && !c.IsDeleted, ct);

            if (contact != null)
            {
                matchedContactId = contact.Id;
                matchedAccountId = contact.AccountId;

                if (settings.MatchDeals)
                {
                    var deal = await db.Set<Deal>()
                        .Where(d => d.Status == "Open" && !d.IsDeleted && (d.AccountId == matchedAccountId || d.PrimaryContactId == matchedContactId))
                        .OrderByDescending(d => d.UpdatedAt)
                        .FirstOrDefaultAsync(ct);

                    if (deal != null)
                    {
                        matchedDealId = deal.Id;
                    }
                }
                break;
            }
        }

        // 5. Handle un-matched recipients: auto-create if enabled
        if (matchedLeadId == null && matchedContactId == null && toNormList.Count > 0)
        {
            var firstRecipient = toNormList[0];
            var domain = ExtractDomain(firstRecipient);
            var namePart = firstRecipient.Split('@')[0];

            if (settings.AutoCreateLead)
            {
                var newLead = new Lead
                {
                    TenantId = targetTenantId,
                    Email = firstRecipient,
                    FirstName = char.ToUpperInvariant(namePart[0]) + (namePart.Length > 1 ? namePart.Substring(1) : ""),
                    LastName = "Lead",
                    CompanyName = !string.IsNullOrEmpty(domain) ? char.ToUpperInvariant(domain[0]) + (domain.Length > 1 ? domain.Substring(1) : "") : "Unknown",
                    Status = "new",
                    OwnerId = address.UserId
                };
                db.Set<Lead>().Add(newLead);
                await db.SaveChangesAsync(ct);
                matchedLeadId = newLead.Id;
                actionTaken = "CreatedLead";
            }
            else if (settings.AutoCreateContact)
            {
                var newContact = new Contact
                {
                    TenantId = targetTenantId,
                    Email = firstRecipient,
                    FirstName = char.ToUpperInvariant(namePart[0]) + (namePart.Length > 1 ? namePart.Substring(1) : ""),
                    LastName = "Contact",
                    OwnerId = address.UserId
                };
                db.Set<Contact>().Add(newContact);
                await db.SaveChangesAsync(ct);
                matchedContactId = newContact.Id;
                actionTaken = "CreatedContact";
            }
            else
            {
                actionTaken = "LoggedUnmatched";
            }
        }

        // 6. Create Message record if matched or if LogNonMatched is true
        Guid? createdMessageId = null;
        if (matchedLeadId != null || matchedContactId != null || settings.LogNonMatched)
        {
            var message = new Message
            {
                TenantId = targetTenantId,
                Channel = CommunicationChannel.Email,
                Direction = MessageDirection.Outbound,
                Status = MessageStatus.Sent,
                From = request.From,
                To = string.Join(", ", request.To),
                Subject = request.Subject ?? "(No Subject)",
                Body = request.BodyPlain ?? request.BodyHtml ?? string.Empty,
                ThreadId = Guid.NewGuid(),
                ExternalMessageId = request.MessageId,
                LeadId = matchedLeadId,
                ContactId = matchedContactId,
                AccountId = matchedAccountId,
                DealId = matchedDealId,
                SentByUserId = address.UserId,
                DeliveredAt = request.DateUtc ?? DateTime.UtcNow
            };
            db.Set<Message>().Add(message);
            await db.SaveChangesAsync(ct);
            createdMessageId = message.Id;

            // Publish timeline event
            try
            {
                if (matchedLeadId != null)
                {
                    await timeline.PublishAsync(new TimelineEventInput(
                        "lead",
                        matchedLeadId.Value,
                        "email_sent",
                        $"BCC logged email: {request.Subject ?? "(No Subject)"}",
                        "bcc_dropbox"), ct);
                }
                else if (matchedContactId != null)
                {
                    await timeline.PublishAsync(new TimelineEventInput(
                        "contact",
                        matchedContactId.Value,
                        "email_sent",
                        $"BCC logged email: {request.Subject ?? "(No Subject)"}",
                        "bcc_dropbox"), ct);
                }

                if (matchedDealId != null)
                {
                    await timeline.PublishAsync(new TimelineEventInput(
                        "deal",
                        matchedDealId.Value,
                        "email_sent",
                        $"BCC logged email: {request.Subject ?? "(No Subject)"}",
                        "bcc_dropbox"), ct);
                }
            }
            catch
            {
                // Non-fatal timeline publication
            }
        }

        // 7. Update address counters and create log
        address.TotalEmailsReceived++;
        address.LastReceivedAtUtc = DateTime.UtcNow;

        var log = new BccDropboxLog
        {
            TenantId = targetTenantId,
            BccDropboxAddressId = address.Id,
            UserId = address.UserId,
            MessageId = request.MessageId ?? Guid.NewGuid().ToString(),
            FromAddress = request.From,
            RecipientAddresses = request.To,
            Subject = request.Subject ?? string.Empty,
            MatchedLeadId = matchedLeadId,
            MatchedContactId = matchedContactId,
            MatchedAccountId = matchedAccountId,
            MatchedDealId = matchedDealId,
            ActionTaken = actionTaken,
            ReceivedAtUtc = DateTime.UtcNow,
            CreatedMessageId = createdMessageId
        };
        db.BccDropboxLogs.Add(log);
        await db.SaveChangesAsync(ct);

        return new InboundBccEmailResult(
            true,
            actionTaken,
            matchedLeadId,
            matchedContactId,
            matchedAccountId,
            matchedDealId,
            createdMessageId,
            "Email processed successfully via BCC dropbox.");
    }

    public async Task<IReadOnlyList<BccDropboxLogDto>> ListLogsAsync(int limit = 50, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        return await db.BccDropboxLogs
            .OrderByDescending(l => l.ReceivedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(l => new BccDropboxLogDto(
                l.Id,
                l.UserId,
                l.FromAddress,
                l.RecipientAddresses,
                l.Subject,
                l.MatchedLeadId,
                l.MatchedContactId,
                l.MatchedAccountId,
                l.MatchedDealId,
                l.ActionTaken,
                l.ReceivedAtUtc))
            .ToListAsync(ct);
    }

    private async Task<BccDropboxSettings> GetSettingsEntityAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.BccDropboxSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (settings == null)
        {
            settings = new BccDropboxSettings
            {
                TenantId = tenantId,
                DropboxDomain = "bcc.mindlift.crm",
                AutoCreateLead = false,
                AutoCreateContact = false,
                MatchDeals = true,
                DefaultPrivacy = "Public",
                ExcludedDomains = [],
                LogNonMatched = true
            };
            db.BccDropboxSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        return settings;
    }
}

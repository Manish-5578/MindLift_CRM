using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #27: GDPR and unsubscribe center (unsubscribe-center).
/// Implements GDPR Article 7 consent records, self-service subscription preferences,
/// RFC 8058 one-click unsubscribe, Article 15/20 data exports, and Article 17 erasure.
/// </summary>
public sealed class GdprConsentService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ILogger<GdprConsentService> logger) : IGdprConsentService
{
    private static readonly string[] StandardTopics = ["Marketing", "Sales", "ProductUpdates", "Surveys"];

    public async Task<PreferenceCenterDetailsDto?> GetPreferenceCenterAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var tokenRecord = await db.Set<SubscriptionPreferenceToken>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == token.Trim() && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow, ct);

        if (tokenRecord == null) return null;

        using var scope = tenantContext.Use(tokenRecord.TenantId);
        var email = tokenRecord.Email.Trim().ToLowerInvariant();

        // Check global suppression
        var isSuppressed = await db.Set<EmailSuppression>()
            .AsNoTracking()
            .AnyAsync(s => s.Email == email, ct);

        // Fetch latest consent status per topic
        var consentRecords = await db.Set<GdprConsentRecord>()
            .AsNoTracking()
            .Where(c => c.Email == email && c.Channel == CommunicationChannel.Email)
            .OrderByDescending(c => c.EffectiveAt)
            .ToListAsync(ct);

        var globalOptOut = isSuppressed || consentRecords.Any(c => c.Topic == "All" && c.Status == ConsentStatus.OptedOut);

        var topicItems = new List<TopicPreferenceItem>();

        foreach (var topic in StandardTopics)
        {
            var latest = consentRecords.FirstOrDefault(c => string.Equals(c.Topic, topic, StringComparison.OrdinalIgnoreCase));
            var isSubscribed = !globalOptOut && (latest == null || latest.Status != ConsentStatus.OptedOut);

            topicItems.Add(new TopicPreferenceItem
            {
                Topic = topic,
                DisplayName = GetTopicDisplayName(topic),
                Description = GetTopicDescription(topic),
                IsSubscribed = isSubscribed
            });
        }

        var lastUpdated = consentRecords.Count > 0 ? consentRecords.Max(c => c.EffectiveAt) : tokenRecord.CreatedAt;

        return new PreferenceCenterDetailsDto
        {
            Email = tokenRecord.Email,
            GlobalUnsubscribe = globalOptOut,
            Topics = topicItems,
            LastUpdatedAt = lastUpdated
        };
    }

    public async Task<bool> UpdatePreferencesAsync(
        string token,
        UpdatePreferencesRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var tokenRecord = await db.Set<SubscriptionPreferenceToken>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == token.Trim() && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow, ct);

        if (tokenRecord == null) return false;

        using var scope = tenantContext.Use(tokenRecord.TenantId);
        var email = tokenRecord.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        if (request.GlobalUnsubscribe)
        {
            // 1. Add EmailSuppression
            var suppression = await db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Email == email, ct);
            if (suppression == null)
            {
                db.Set<EmailSuppression>().Add(new EmailSuppression
                {
                    Email = email,
                    Reason = "unsubscribed"
                });
            }

            // 2. Add CommunicationOptOut
            var commOptOut = await db.Set<CommunicationOptOut>().FirstOrDefaultAsync(o => o.Target == email && o.Channel == CommunicationChannel.Email, ct);
            if (commOptOut == null)
            {
                db.Set<CommunicationOptOut>().Add(new CommunicationOptOut
                {
                    Target = email,
                    Channel = CommunicationChannel.Email,
                    Reason = request.Reason ?? "Global Unsubscribe via Preference Center"
                });
            }

            // 3. Add GDPR Consent record
            db.Set<GdprConsentRecord>().Add(new GdprConsentRecord
            {
                Email = email,
                Channel = CommunicationChannel.Email,
                Topic = "All",
                Status = ConsentStatus.OptedOut,
                LawfulBasis = GdprLawfulBasis.Consent,
                Source = "UnsubscribeCenter",
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Notes = request.Reason ?? "Global unsubscribe selected in preference center",
                EffectiveAt = now
            });
        }
        else
        {
            // Remove global suppression if previously suppressed
            var existingSuppression = await db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Email == email, ct);
            if (existingSuppression != null && existingSuppression.Reason != "bounced" && existingSuppression.Reason != "complained" && existingSuppression.Reason != "gdpr_erasure")
            {
                existingSuppression.IsDeleted = true;
            }

            // Record topic-level preferences
            foreach (var kvp in request.TopicPreferences)
            {
                var topic = kvp.Key.Trim();
                var status = kvp.Value ? ConsentStatus.OptedIn : ConsentStatus.OptedOut;

                db.Set<GdprConsentRecord>().Add(new GdprConsentRecord
                {
                    Email = email,
                    Channel = CommunicationChannel.Email,
                    Topic = topic,
                    Status = status,
                    LawfulBasis = GdprLawfulBasis.Consent,
                    Source = "UnsubscribeCenter",
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    EffectiveAt = now
                });
            }
        }

        tokenRecord.LastUsedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Updated communication preferences for {Email} (GlobalOptOut: {GlobalOptOut}).",
            email, request.GlobalUnsubscribe);

        return true;
    }

    public async Task<bool> ProcessOneClickUnsubscribeAsync(
        string token,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var tokenRecord = await db.Set<SubscriptionPreferenceToken>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == token.Trim() && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow, ct);

        if (tokenRecord == null) return false;

        using var scope = tenantContext.Use(tokenRecord.TenantId);
        var email = tokenRecord.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        // 1. Ensure suppressed
        var suppression = await db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Email == email, ct);
        if (suppression == null)
        {
            db.Set<EmailSuppression>().Add(new EmailSuppression
            {
                Email = email,
                Reason = "unsubscribed"
            });
        }

        // 2. Audit RFC 8058 Consent record
        db.Set<GdprConsentRecord>().Add(new GdprConsentRecord
        {
            Email = email,
            Channel = CommunicationChannel.Email,
            Topic = "Marketing",
            Status = ConsentStatus.OptedOut,
            LawfulBasis = GdprLawfulBasis.Consent,
            Source = "OneClickRfc8058",
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Notes = "RFC 8058 One-Click Unsubscribe Header executed",
            EffectiveAt = now
        });

        tokenRecord.LastUsedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Processed RFC 8058 One-Click Unsubscribe for {Email}.", email);
        return true;
    }

    public async Task<PreferenceTokenDto> GeneratePreferenceTokenAsync(
        GeneratePreferenceTokenRequest request,
        CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var token = GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.AddDays(90);

        var tokenEntity = new SubscriptionPreferenceToken
        {
            Token = token,
            Email = email,
            LeadId = request.LeadId,
            ContactId = request.ContactId,
            ExpiresAt = expiresAt,
            IsRevoked = false
        };

        db.Set<SubscriptionPreferenceToken>().Add(tokenEntity);
        await db.SaveChangesAsync(ct);

        var prefUrl = $"https://crm.mindlift.io/preferences?token={token}";
        var oneClickUrl = $"https://crm.mindlift.io/api/v1/consent/one-click-unsubscribe?token={token}";

        return new PreferenceTokenDto
        {
            Token = token,
            Email = email,
            PreferenceCenterUrl = prefUrl,
            OneClickUnsubscribeUrl = oneClickUrl,
            ExpiresAt = expiresAt
        };
    }

    public async Task<ConsentCheckResult> CheckConsentAsync(
        string email,
        CommunicationChannel channel = CommunicationChannel.Email,
        string topic = "Marketing",
        CancellationToken ct = default)
    {
        var normEmail = email.Trim().ToLowerInvariant();

        // 1. Check suppression list
        var isSuppressed = await db.Set<EmailSuppression>()
            .AsNoTracking()
            .AnyAsync(s => s.Email == normEmail, ct);

        if (isSuppressed)
        {
            return new ConsentCheckResult
            {
                Email = normEmail,
                Channel = channel,
                Topic = topic,
                IsAllowed = false,
                Reason = "Address is registered in tenant email suppressions.",
                CurrentStatus = ConsentStatus.OptedOut
            };
        }

        // 2. Check channel opt-outs
        var isChannelOptedOut = await db.Set<CommunicationOptOut>()
            .AsNoTracking()
            .AnyAsync(o => o.Target == normEmail && (o.Channel == null || o.Channel == channel), ct);

        if (isChannelOptedOut)
        {
            return new ConsentCheckResult
            {
                Email = normEmail,
                Channel = channel,
                Topic = topic,
                IsAllowed = false,
                Reason = $"Address is opted out from channel {channel}.",
                CurrentStatus = ConsentStatus.OptedOut
            };
        }

        // 3. Check GDPR consent records
        var latestConsent = await db.Set<GdprConsentRecord>()
            .AsNoTracking()
            .Where(c => c.Email == normEmail && c.Channel == channel && (c.Topic == topic || c.Topic == "All"))
            .OrderByDescending(c => c.EffectiveAt)
            .FirstOrDefaultAsync(ct);

        if (latestConsent != null)
        {
            return new ConsentCheckResult
            {
                Email = normEmail,
                Channel = channel,
                Topic = topic,
                IsAllowed = latestConsent.Status == ConsentStatus.OptedIn,
                Reason = latestConsent.Status == ConsentStatus.OptedIn
                    ? "Active opt-in consent recorded."
                    : "Explicit opt-out registered for topic/channel.",
                CurrentStatus = latestConsent.Status
            };
        }

        // Default allow for non-suppressed if no prior explicit opt-out
        return new ConsentCheckResult
        {
            Email = normEmail,
            Channel = channel,
            Topic = topic,
            IsAllowed = true,
            Reason = "No negative suppression or explicit opt-out found.",
            CurrentStatus = ConsentStatus.Pending
        };
    }

    public async Task<IReadOnlyList<GdprConsentRecordDto>> ListConsentRecordsAsync(
        string? email = null,
        CommunicationChannel? channel = null,
        string? topic = null,
        ConsentStatus? status = null,
        CancellationToken ct = default)
    {
        var query = db.Set<GdprConsentRecord>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(email))
        {
            var norm = email.Trim().ToLowerInvariant();
            query = query.Where(c => c.Email.ToLower() == norm);
        }

        if (channel.HasValue)
        {
            query = query.Where(c => c.Channel == channel.Value);
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var top = topic.Trim().ToLowerInvariant();
            query = query.Where(c => c.Topic.ToLower() == top);
        }

        if (status.HasValue)
        {
            query = query.Where(c => c.Status == status.Value);
        }

        return await query
            .OrderByDescending(c => c.EffectiveAt)
            .Select(c => new GdprConsentRecordDto
            {
                Id = c.Id,
                TenantId = c.TenantId,
                Email = c.Email,
                Channel = c.Channel,
                Topic = c.Topic,
                Status = c.Status,
                LawfulBasis = c.LawfulBasis,
                Source = c.Source,
                IpAddress = c.IpAddress,
                UserAgent = c.UserAgent,
                Notes = c.Notes,
                EffectiveAt = c.EffectiveAt,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<GdprConsentRecordDto> SaveConsentRecordAsync(
        SaveConsentRecordRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var record = new GdprConsentRecord
        {
            Email = email,
            Channel = request.Channel,
            Topic = string.IsNullOrWhiteSpace(request.Topic) ? "Marketing" : request.Topic.Trim(),
            Status = request.Status,
            LawfulBasis = request.LawfulBasis,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "ManualAdmin" : request.Source.Trim(),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Notes = request.Notes,
            EffectiveAt = DateTime.UtcNow
        };

        db.Set<GdprConsentRecord>().Add(record);

        // Synchronize with email suppression if OptedOut
        if (record.Status == ConsentStatus.OptedOut && record.Channel == CommunicationChannel.Email)
        {
            var suppression = await db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Email == email, ct);
            if (suppression == null)
            {
                db.Set<EmailSuppression>().Add(new EmailSuppression
                {
                    Email = email,
                    Reason = "consent_opt_out"
                });
            }
        }

        await db.SaveChangesAsync(ct);

        return new GdprConsentRecordDto
        {
            Id = record.Id,
            TenantId = record.TenantId,
            Email = record.Email,
            Channel = record.Channel,
            Topic = record.Topic,
            Status = record.Status,
            LawfulBasis = record.LawfulBasis,
            Source = record.Source,
            IpAddress = record.IpAddress,
            UserAgent = record.UserAgent,
            Notes = record.Notes,
            EffectiveAt = record.EffectiveAt,
            CreatedAt = record.CreatedAt
        };
    }

    public async Task<GdprSubjectDataExport> ExportSubjectDataAsync(string email, CancellationToken ct = default)
    {
        var normEmail = email.Trim().ToLowerInvariant();

        var leadRecords = await db.Set<Lead>()
            .AsNoTracking()
            .Where(l => l.Email != null && l.Email.ToLower() == normEmail)
            .Select(l => new GdprSubjectRecord
            {
                Id = l.Id,
                RecordType = "Lead",
                FirstName = l.FirstName,
                LastName = l.LastName,
                Email = l.Email,
                Phone = l.Phone,
                CompanyOrAccount = l.CompanyName,
                Status = l.Status,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync(ct);

        var contactRecords = await db.Set<Contact>()
            .AsNoTracking()
            .Where(c => c.Email != null && c.Email.ToLower() == normEmail)
            .Select(c => new GdprSubjectRecord
            {
                Id = c.Id,
                RecordType = "Contact",
                FirstName = c.FirstName,
                LastName = c.LastName,
                Email = c.Email,
                Phone = c.Phone,
                Title = c.JobTitle,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);

        var consentHistory = await db.Set<GdprConsentRecord>()
            .AsNoTracking()
            .Where(c => c.Email.ToLower() == normEmail)
            .OrderByDescending(c => c.EffectiveAt)
            .Select(c => new GdprConsentRecordDto
            {
                Id = c.Id,
                TenantId = c.TenantId,
                Email = c.Email,
                Channel = c.Channel,
                Topic = c.Topic,
                Status = c.Status,
                LawfulBasis = c.LawfulBasis,
                Source = c.Source,
                IpAddress = c.IpAddress,
                UserAgent = c.UserAgent,
                Notes = c.Notes,
                EffectiveAt = c.EffectiveAt,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);

        var allRecords = new List<GdprSubjectRecord>(leadRecords.Count + contactRecords.Count);
        allRecords.AddRange(leadRecords);
        allRecords.AddRange(contactRecords);

        return new GdprSubjectDataExport
        {
            SubjectEmail = normEmail,
            ExportedAt = DateTime.UtcNow,
            Records = allRecords,
            ConsentHistory = consentHistory
        };
    }

    public async Task<GdprErasureResult> ExecuteErasureAsync(GdprErasureRequest request, CancellationToken ct = default)
    {
        var normEmail = request.Email.Trim().ToLowerInvariant();
        int recordsModified = 0;

        // 1. Anonymize Leads
        var leads = await db.Set<Lead>()
            .Where(l => l.Email != null && l.Email.ToLower() == normEmail)
            .ToListAsync(ct);

        foreach (var lead in leads)
        {
            lead.FirstName = "[Erased]";
            lead.LastName = "[GDPR Erased]";
            lead.Email = $"erased_{Guid.NewGuid():N}@anonymized.crm";
            lead.NormalizedEmail = null;
            lead.Phone = null;
            lead.NormalizedPhone = null;
            lead.CompanyName = "[Redacted]";
            lead.Description = "[Erased per GDPR Article 17]";
            lead.Tags = [];
            recordsModified++;
        }

        // 2. Anonymize Contacts
        var contacts = await db.Set<Contact>()
            .Where(c => c.Email != null && c.Email.ToLower() == normEmail)
            .ToListAsync(ct);

        foreach (var contact in contacts)
        {
            contact.FirstName = "[Erased]";
            contact.LastName = "[GDPR Erased]";
            contact.Email = $"erased_{Guid.NewGuid():N}@anonymized.crm";
            contact.NormalizedEmail = null;
            contact.Phone = null;
            contact.NormalizedPhone = null;
            contact.JobTitle = null;
            contact.Description = "[Erased per GDPR Article 17]";
            recordsModified++;
        }

        // 3. Clear Visitor Sessions
        var sessions = await db.Set<VisitorSession>()
            .Where(s => s.IdentifiedEmail != null && s.IdentifiedEmail.ToLower() == normEmail)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.IdentifiedEmail = null;
            session.IpAddress = "0.0.0.0";
            session.UserAgent = "[Redacted]";
            recordsModified++;
        }

        // 4. Permanent suppression tombstone
        var suppression = await db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Email == normEmail, ct);
        if (suppression == null)
        {
            db.Set<EmailSuppression>().Add(new EmailSuppression
            {
                Email = normEmail,
                Reason = "gdpr_erasure"
            });
        }
        else
        {
            suppression.Reason = "gdpr_erasure";
            suppression.IsDeleted = false;
        }

        // 5. Audit record
        var audit = new GdprErasureRecord
        {
            Email = normEmail,
            RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "DataSubject" : request.RequestedBy.Trim(),
            Status = "Completed",
            RecordsErased = recordsModified,
            Summary = $"Erased {leads.Count} leads, {contacts.Count} contacts, {sessions.Count} browsing sessions.",
            ExecutedAt = DateTime.UtcNow
        };
        db.Set<GdprErasureRecord>().Add(audit);

        await db.SaveChangesAsync(ct);

        logger.LogWarning("GDPR Article 17 Erasure executed for {Email}: {Count} records sanitized.",
            normEmail, recordsModified);

        return new GdprErasureResult
        {
            Email = normEmail,
            RecordsErased = recordsModified,
            Summary = audit.Summary,
            ExecutedAt = audit.ExecutedAt
        };
    }

    private static string GenerateSecureToken()
    {
        return Guid.NewGuid().ToString("N") + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    private static string GetTopicDisplayName(string topic) => topic switch
    {
        "Marketing" => "Marketing & Newsletters",
        "Sales" => "Direct Sales & Outreach",
        "ProductUpdates" => "Product Updates & Announcements",
        "Surveys" => "Feedback & Customer Surveys",
        _ => topic
    };

    private static string GetTopicDescription(string topic) => topic switch
    {
        "Marketing" => "Product launches, webinars, special offers, and newsletter digests.",
        "Sales" => "Personalized communications from your dedicated account team.",
        "ProductUpdates" => "Critical release notes, new feature alerts, and platform enhancements.",
        "Surveys" => "Occasional surveys to help improve our products and services.",
        _ => "Subscription updates."
    };
}

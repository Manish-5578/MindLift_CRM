using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Messaging;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Campaigns;

/// <summary>
/// Email campaigns: build the message and the audience, preview who would get it, send a test, send now or at a time, and see how it did.
/// Sending goes through <see cref="EmailGuard"/>, so people who unsubscribed are never mailed and a company cannot send more than its daily limit;
/// what does not fit today waits and goes out the next day.
/// </summary>
public sealed partial class CampaignService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IBackgroundJobClient jobs,
    IEmailSender email,
    EmailGuard guard,
    INotificationService notifications,
    IConfiguration configuration)
{
    public const int MaxRecipients = 50_000;

    private string BaseUrl => (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');

    // ------------------------------------------------------------------ reading

    public async Task<IReadOnlyList<CampaignListItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var campaigns = await db.Campaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var stats = await StatsAsync(campaigns.Select(c => c.Id).ToList(), cancellationToken);
        return campaigns.Select(c => new CampaignListItemDto(c.Id, c.Name, c.Subject, c.Status, c.AudienceType, c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CreatedAt, stats.GetValueOrDefault(c.Id) ?? Empty)).ToList();
    }

    public async Task<CampaignDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        return Map(c, (await StatsAsync([id], cancellationToken)).GetValueOrDefault(id) ?? Empty);
    }

    public async Task<PagedResult<CampaignRecipientDto>> RecipientsAsync(Guid id, string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var source = db.CampaignRecipients.AsNoTracking().Where(r => r.CampaignId == id);
        if (!string.IsNullOrWhiteSpace(status))
        {
            source = status switch
            {
                "opened" => source.Where(r => r.OpenedAt != null),
                "clicked" => source.Where(r => r.ClickedAt != null),
                "unsubscribed" => source.Where(r => r.UnsubscribedAt != null),
                _ => source.Where(r => r.Status == status),
            };
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderBy(r => r.Email).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<CampaignRecipientDto>(rows.Select(r => new CampaignRecipientDto(r.Id, r.Email, $"{r.FirstName} {r.LastName}".Trim(), r.Status, r.Reason, r.SentAt, r.OpenedAt, r.ClickedAt, r.UnsubscribedAt)).ToList(), total, page, pageSize);
    }

    public async Task<AudiencePreviewDto> PreviewAsync(CampaignAudience audience, CancellationToken cancellationToken = default)
    {
        var people = await AudienceAsync(audience, cancellationToken);
        var suppressed = (await db.EmailSuppressions.AsNoTracking().Select(s => s.Email).ToListAsync(cancellationToken)).ToHashSet();
        var withEmail = people.Where(p => !string.IsNullOrWhiteSpace(p.Email)).ToList();
        var reachable = withEmail.Where(p => !suppressed.Contains(p.Email.Trim().ToLowerInvariant())).DistinctBy(p => p.Email.Trim().ToLowerInvariant()).ToList();
        return new AudiencePreviewDto(people.Count, reachable.Count, people.Count - withEmail.Count, withEmail.Count - reachable.Count - (withEmail.Count - withEmail.DistinctBy(p => p.Email.Trim().ToLowerInvariant()).Count()),
            reachable.Take(5).Select(p => $"{p.FirstName} {p.LastName}".Trim() + " <" + p.Email + ">").ToList());
    }

    // ------------------------------------------------------------------ writing

    public async Task<CampaignDto> CreateAsync(SaveCampaignRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, requireContent: false);
        var campaign = new Campaign();
        Apply(campaign, request);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        return Map(campaign, Empty);
    }

    public async Task<CampaignDto> UpdateAsync(Guid id, SaveCampaignRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, requireContent: false);
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        if (campaign.Status is not (CampaignStatuses.Draft or CampaignStatuses.Scheduled))
        {
            throw new BusinessRuleException("campaign_locked", "A campaign that has started cannot be edited. Copy it to send a changed version.");
        }

        Apply(campaign, request);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CampaignDto> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        var copy = new Campaign
        {
            Name = $"{source.Name} (copy)", Subject = source.Subject, Body = source.Body, AudienceType = source.AudienceType,
            AudienceStatus = source.AudienceStatus, AudienceSource = source.AudienceSource, AudienceTag = source.AudienceTag, AudienceContactType = source.AudienceContactType,
        };
        db.Campaigns.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        return Map(copy, Empty);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        if (campaign.Status == CampaignStatuses.Sending)
        {
            throw new BusinessRuleException("campaign_sending", "A campaign that is being sent cannot be deleted. Cancel it first.");
        }

        db.Campaigns.Remove(campaign);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sends one copy to the address given, marked as a test, so the writer can see the result. Counts for nothing in the results.</summary>
    public async Task SendTestAsync(Guid id, SendTestRequest request, CancellationToken cancellationToken = default)
    {
        var to = (request.To ?? string.Empty).Trim();
        if (!to.Contains('@') || to.Length > 320)
        {
            throw new ValidationException("to", "Enter an email address.");
        }

        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        RequireContent(c);
        var sample = new CampaignRecipient { FirstName = "Alex", LastName = "Example", Company = "Example Co", Email = to, Token = "test" };
        var (text, html) = Compose(c, sample, tracking: false, BaseUrl, guard.Footer(to));
        await email.SendHtmlAsync(to, "[Test] " + Merge(c.Subject, sample), text, html, cancellationToken);
    }

    /// <summary>Starts sending now.</summary>
    public async Task<CampaignDto> StartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        if (campaign.Status is not (CampaignStatuses.Draft or CampaignStatuses.Scheduled))
        {
            throw new BusinessRuleException("campaign_started", "This campaign has already been sent or cancelled.");
        }

        RequireContent(campaign);
        await BeginAsync(campaign, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CampaignDto> ScheduleAsync(Guid id, ScheduleCampaignRequest request, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        if (campaign.Status is not (CampaignStatuses.Draft or CampaignStatuses.Scheduled))
        {
            throw new BusinessRuleException("campaign_started", "This campaign has already been sent or cancelled.");
        }

        if (request.At is null)
        {
            campaign.Status = CampaignStatuses.Draft;
            campaign.ScheduledAt = null;
        }
        else
        {
            RequireContent(campaign);
            var at = DateTime.SpecifyKind(request.At.Value, DateTimeKind.Utc);
            if (at < clock.UtcNow.AddMinutes(-1))
            {
                throw new ValidationException("at", "Choose a time in the future.");
            }

            campaign.Status = CampaignStatuses.Scheduled;
            campaign.ScheduledAt = at;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CampaignDto> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Campaign", id);
        if (campaign.Status is CampaignStatuses.Sent or CampaignStatuses.Cancelled)
        {
            return await GetAsync(id, cancellationToken);
        }

        campaign.Status = CampaignStatuses.Cancelled;
        campaign.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // ------------------------------------------------------------------ sending

    /// <summary>Fixes who gets the campaign (everyone reachable right now) and hands the sending to a background job.</summary>
    private async Task BeginAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        var people = await AudienceAsync(Audience(campaign), cancellationToken);
        var seen = new HashSet<string>();
        var added = 0;
        var suppressed = (await db.EmailSuppressions.AsNoTracking().Select(s => s.Email).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var p in people.Where(p => !string.IsNullOrWhiteSpace(p.Email)))
        {
            var address = p.Email.Trim().ToLowerInvariant();
            if (!seen.Add(address) || suppressed.Contains(address))
            {
                continue;
            }

            if (++added > MaxRecipients)
            {
                throw new ValidationException("audience", $"A campaign can go to at most {MaxRecipients:N0} people. Narrow the audience.");
            }

            db.CampaignRecipients.Add(new CampaignRecipient
            {
                CampaignId = campaign.Id, RecordType = campaign.AudienceType == "contacts" ? "contact" : "lead", RecordId = p.Id, Email = address,
                FirstName = p.FirstName, LastName = p.LastName, Company = p.Company, Token = NewToken(),
            });
        }

        if (added == 0)
        {
            throw new BusinessRuleException("no_audience", "Nobody in this audience can be emailed. Check the filters.");
        }

        campaign.Status = CampaignStatuses.Sending;
        campaign.StartedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<CampaignSendJob>("messaging", job => job.RunAsync(campaign.TenantId, campaign.Id, CancellationToken.None));
    }

    internal static (string Text, string Html) Compose(Campaign c, CampaignRecipient r, bool tracking, string baseUrl, string footer)
    {
        var text = Merge(c.Body, r) + footer;
        var sb = new StringBuilder("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.55;color:#0F172A\">");
        foreach (var line in Merge(c.Body, r).Replace("\r\n", "\n").Split('\n'))
        {
            sb.Append(Linkify(WebUtility.HtmlEncode(line), r.Token, tracking, baseUrl)).Append("<br>");
        }

        sb.Append("<hr style=\"border:0;border-top:1px solid #E2E8F0;margin:24px 0 12px\"><div style=\"font-size:12px;color:#64748B\">")
          .Append(Linkify(WebUtility.HtmlEncode(footer.Trim().TrimStart('-').Trim()).Replace("\n", "<br>"), r.Token, false, baseUrl)).Append("</div>");
        if (tracking)
        {
            sb.Append($"<img src=\"{baseUrl}/t/o/{r.Token}.gif\" width=\"1\" height=\"1\" alt=\"\" style=\"display:none\">");
        }

        return (text, sb.Append("</div>").ToString());
    }

    private static string Linkify(string encodedLine, string token, bool tracking, string baseUrl)
        => UrlPattern().Replace(encodedLine, m =>
        {
            var url = WebUtility.HtmlDecode(m.Value).TrimEnd('.', ',', ')', ';');
            var trail = m.Value[(m.Value.TrimEnd('.', ',', ')', ';').Length)..];
            var href = tracking ? $"{baseUrl}/t/c/{token}?u={Uri.EscapeDataString(url)}" : url;
            return $"<a href=\"{WebUtility.HtmlEncode(href)}\" style=\"color:#4F46E5\">{WebUtility.HtmlEncode(url)}</a>{trail}";
        });

    internal static string Merge(string template, CampaignRecipient r)
        => (template ?? string.Empty)
            .Replace("{{first_name}}", r.FirstName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{last_name}}", r.LastName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{company}}", r.Company ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{{email}}", r.Email, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> LinksIn(string body) => UrlPattern().Matches(body ?? string.Empty).Select(m => m.Value.TrimEnd('.', ',', ')', ';')).Distinct().ToList();

    // ------------------------------------------------------------------ helpers

    private sealed record Person(Guid Id, string FirstName, string LastName, string? Company, string Email);

    private async Task<List<Person>> AudienceAsync(CampaignAudience audience, CancellationToken cancellationToken)
    {
        if (audience.Type == "contacts")
        {
            var contacts = db.Contacts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(audience.ContactType)) contacts = contacts.Where(c => c.Type == audience.ContactType);
            var rows = await contacts.Select(c => new { c.Id, c.FirstName, c.LastName, c.AccountId, c.Email }).ToListAsync(cancellationToken);
            var accountIds = rows.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList();
            var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
            return rows.Select(r => new Person(r.Id, r.FirstName, r.LastName, r.AccountId is { } a ? accounts.GetValueOrDefault(a) : null, r.Email ?? string.Empty)).ToList();
        }

        var leads = db.Leads.AsNoTracking().Where(l => l.ConvertedAt == null);
        if (!string.IsNullOrWhiteSpace(audience.Status)) leads = leads.Where(l => l.Status == audience.Status);
        if (!string.IsNullOrWhiteSpace(audience.Source)) leads = leads.Where(l => l.Source == audience.Source);
        if (!string.IsNullOrWhiteSpace(audience.Tag)) leads = leads.Where(l => l.Tags.Contains(audience.Tag));
        return (await leads.Select(l => new { l.Id, l.FirstName, l.LastName, l.CompanyName, l.Email }).ToListAsync(cancellationToken))
            .Select(l => new Person(l.Id, l.FirstName, l.LastName, l.CompanyName, l.Email ?? string.Empty)).ToList();
    }

    private async Task<Dictionary<Guid, CampaignStatsDto>> StatsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await db.CampaignRecipients.AsNoTracking().Where(r => ids.Contains(r.CampaignId))
            .GroupBy(r => r.CampaignId)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Sent = g.Count(r => r.Status == "sent"),
                Failed = g.Count(r => r.Status == "failed"),
                Skipped = g.Count(r => r.Status == "skipped"),
                Pending = g.Count(r => r.Status == "pending"),
                Opened = g.Count(r => r.OpenedAt != null),
                Clicked = g.Count(r => r.ClickedAt != null),
                Unsub = g.Count(r => r.UnsubscribedAt != null),
            }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => new CampaignStatsDto(r.Total, r.Sent, r.Failed, r.Skipped, r.Pending, r.Opened, r.Clicked, r.Unsub));
    }

    private static readonly CampaignStatsDto Empty = new(0, 0, 0, 0, 0, 0, 0, 0);

    private static CampaignAudience Audience(Campaign c) => new(c.AudienceType, c.AudienceStatus, c.AudienceSource, c.AudienceTag, c.AudienceContactType);

    private static CampaignDto Map(Campaign c, CampaignStatsDto stats)
        => new(c.Id, c.Name, c.Subject, c.Body, c.Status, Audience(c), c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CreatedAt, stats);

    private static void Apply(Campaign c, SaveCampaignRequest r)
    {
        c.Name = r.Name.Trim();
        c.Subject = (r.Subject ?? string.Empty).Trim();
        c.Body = r.Body ?? string.Empty;
        c.AudienceType = r.Audience.Type == "contacts" ? "contacts" : "leads";
        c.AudienceStatus = Clean(r.Audience.Status);
        c.AudienceSource = Clean(r.Audience.Source);
        c.AudienceTag = Clean(r.Audience.Tag);
        c.AudienceContactType = Clean(r.Audience.ContactType);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static void Validate(SaveCampaignRequest r, bool requireContent)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 150) errors["name"] = ["Enter a name of up to 150 characters."];
        if ((r.Subject ?? string.Empty).Length > 200) errors["subject"] = ["The subject can be up to 200 characters."];
        if ((r.Body ?? string.Empty).Length > 50_000) errors["body"] = ["The message can be up to 50,000 characters."];
        if (r.Audience is null || r.Audience.Type is not ("leads" or "contacts")) errors["audience"] = ["Choose leads or contacts."];
        if (requireContent && (string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrWhiteSpace(r.Body))) errors["subject"] = ["Write a subject and a message."];
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static void RequireContent(Campaign c)
    {
        if (string.IsNullOrWhiteSpace(c.Subject) || string.IsNullOrWhiteSpace(c.Body))
        {
            throw new ValidationException("subject", "Write a subject and a message first.");
        }
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}

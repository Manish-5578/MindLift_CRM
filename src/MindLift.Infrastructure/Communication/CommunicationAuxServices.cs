using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed partial class OptOutService(AppDbContext dbContext) : IOptOutService
{
    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitsRegex();

    public static string NormalizeTarget(string target, CommunicationChannel? channel)
    {
        var trimmed = target.Trim();
        if (channel == CommunicationChannel.Email || (channel == null && trimmed.Contains('@')))
        {
            return trimmed.ToLowerInvariant();
        }

        // Phone normalization
        var digits = NonDigitsRegex().Replace(trimmed, "");
        return digits.Length > 0 ? "+" + digits : trimmed;
    }

    public async Task<bool> IsOptedOutAsync(string target, CommunicationChannel channel, CancellationToken ct = default)
    {
        var normalized = NormalizeTarget(target, channel);
        return await dbContext.CommunicationOptOuts
            .AnyAsync(o => o.Target == normalized && (o.Channel == null || o.Channel == channel), ct);
    }

    public async Task<List<OptOutDto>> GetOptOutsAsync(CancellationToken ct = default)
    {
        var list = await dbContext.CommunicationOptOuts
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        return list.Select(o => new OptOutDto(o.Id, o.Channel, o.Target, o.Reason, o.CreatedAt)).ToList();
    }

    public async Task<OptOutDto> RegisterOptOutAsync(RegisterOptOutRequest request, CancellationToken ct = default)
    {
        var normalized = NormalizeTarget(request.Target, request.Channel);
        var existing = await dbContext.CommunicationOptOuts
            .FirstOrDefaultAsync(o => o.Target == normalized && o.Channel == request.Channel, ct);

        if (existing != null)
        {
            existing.Reason = request.Reason ?? existing.Reason;
            await dbContext.SaveChangesAsync(ct);
            return new OptOutDto(existing.Id, existing.Channel, existing.Target, existing.Reason, existing.CreatedAt);
        }

        var optOut = new CommunicationOptOut
        {
            Channel = request.Channel,
            Target = normalized,
            Reason = request.Reason
        };

        dbContext.CommunicationOptOuts.Add(optOut);
        await dbContext.SaveChangesAsync(ct);

        return new OptOutDto(optOut.Id, optOut.Channel, optOut.Target, optOut.Reason, optOut.CreatedAt);
    }

    public async Task RemoveOptOutAsync(Guid id, CancellationToken ct = default)
    {
        var optOut = await dbContext.CommunicationOptOuts.FindAsync([id], ct);
        if (optOut != null)
        {
            dbContext.CommunicationOptOuts.Remove(optOut);
            await dbContext.SaveChangesAsync(ct);
        }
    }
}

public sealed partial class MessageTemplateService(AppDbContext dbContext) : IMessageTemplateService
{
    [GeneratedRegex(@"\{\{([a-zA-Z0-9_\.]+)\}\}")]
    private static partial Regex PlaceholderRegex();

    public async Task<List<MessageTemplateDto>> GetTemplatesAsync(CommunicationChannel? channel = null, CancellationToken ct = default)
    {
        var query = dbContext.MessageTemplates.AsQueryable();
        if (channel.HasValue)
        {
            query = query.Where(t => t.Channel == channel.Value);
        }

        var list = await query.OrderBy(t => t.Name).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<MessageTemplateDto> GetTemplateByIdAsync(Guid id, CancellationToken ct = default)
    {
        var template = await dbContext.MessageTemplates.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Template {id} not found.");
        return ToDto(template);
    }

    public async Task<MessageTemplateDto> SaveTemplateAsync(Guid? id, SaveMessageTemplateRequest request, CancellationToken ct = default)
    {
        MessageTemplate template;
        if (id.HasValue && id.Value != Guid.Empty)
        {
            template = await dbContext.MessageTemplates.FindAsync([id.Value], ct)
                ?? throw new KeyNotFoundException($"Template {id.Value} not found.");
        }
        else
        {
            template = new MessageTemplate();
            dbContext.MessageTemplates.Add(template);
        }

        template.Channel = request.Channel;
        template.Name = request.Name;
        template.Code = request.Code.Trim().ToLowerInvariant();
        template.Category = request.Category;
        template.Subject = request.Subject;
        template.Body = request.Body;

        await dbContext.SaveChangesAsync(ct);
        return ToDto(template);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await dbContext.MessageTemplates.FindAsync([id], ct);
        if (template != null)
        {
            dbContext.MessageTemplates.Remove(template);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<string> RenderTemplateAsync(Guid templateId, string entityType, Guid entityId, Dictionary<string, string>? extraParams = null, CancellationToken ct = default)
    {
        var template = await dbContext.MessageTemplates.FindAsync([templateId], ct)
            ?? throw new KeyNotFoundException($"Template {templateId} not found.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Fetch entity values
        var type = entityType.Trim().ToLowerInvariant();
        if (type is "lead" or "leads")
        {
            var lead = await dbContext.Leads.FindAsync([entityId], ct);
            if (lead != null)
            {
                values["lead.first_name"] = lead.FirstName;
                values["lead.last_name"] = lead.LastName;
                values["lead.name"] = $"{lead.FirstName} {lead.LastName}".Trim();
                values["lead.email"] = lead.Email ?? "";
                values["lead.phone"] = lead.Phone ?? "";
                values["lead.company"] = lead.CompanyName ?? "";
            }
        }
        else if (type is "contact" or "contacts")
        {
            var contact = await dbContext.Contacts.FindAsync([entityId], ct);
            if (contact != null)
            {
                values["contact.first_name"] = contact.FirstName;
                values["contact.last_name"] = contact.LastName;
                values["contact.name"] = $"{contact.FirstName} {contact.LastName}".Trim();
                values["contact.email"] = contact.Email ?? "";
                values["contact.phone"] = contact.Phone ?? "";
                values["contact.job_title"] = contact.JobTitle ?? "";
            }
        }
        else if (type is "deal" or "deals")
        {
            var deal = await dbContext.Deals.FindAsync([entityId], ct);
            if (deal != null)
            {
                values["deal.name"] = deal.Name;
                values["deal.amount"] = deal.Amount.ToString("N2");
                values["deal.currency"] = deal.Currency;
                values["deal.status"] = deal.Status;
            }
        }

        if (extraParams != null)
        {
            foreach (var kv in extraParams)
            {
                values[kv.Key] = kv.Value;
            }
        }

        return PlaceholderRegex().Replace(template.Body, m =>
        {
            var key = m.Groups[1].Value;
            return values.TryGetValue(key, out var val) ? val : m.Value;
        });
    }

    private static MessageTemplateDto ToDto(MessageTemplate t)
    {
        var matches = PlaceholderRegex().Matches(t.Body);
        var placeholders = matches.Select(m => m.Groups[1].Value).Distinct().ToList();
        return new MessageTemplateDto(t.Id, t.Channel, t.Name, t.Code, t.Category, t.Subject, t.Body, placeholders, t.IsApproved, t.ExternalTemplateId, t.CreatedAt);
    }
}

public sealed class ChannelAccountService(
    AppDbContext dbContext,
    ISecretProtector secretProtector,
    IEnumerable<IChannelSender> senders) : IChannelAccountService
{
    public async Task<List<ChannelAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var list = await dbContext.ChannelAccounts
            .OrderBy(a => a.Channel)
            .ThenByDescending(a => a.IsDefault)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<ChannelAccountDto> GetAccountByIdAsync(Guid id, CancellationToken ct = default)
    {
        var account = await dbContext.ChannelAccounts.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Channel account {id} not found.");
        return ToDto(account);
    }

    public async Task<ChannelAccountDto> SaveAccountAsync(Guid? id, SaveChannelAccountRequest request, CancellationToken ct = default)
    {
        ChannelAccount account;
        if (id.HasValue && id.Value != Guid.Empty)
        {
            account = await dbContext.ChannelAccounts.FindAsync([id.Value], ct)
                ?? throw new KeyNotFoundException($"Channel account {id.Value} not found.");
        }
        else
        {
            account = new ChannelAccount();
            dbContext.ChannelAccounts.Add(account);
        }

        if (request.IsDefault)
        {
            // Reset existing defaults for this channel
            var others = await dbContext.ChannelAccounts
                .Where(a => a.Channel == request.Channel && a.Id != (id ?? Guid.Empty))
                .ToListAsync(ct);
            foreach (var o in others)
            {
                o.IsDefault = false;
            }
        }

        account.Channel = request.Channel;
        account.Name = request.Name;
        account.SenderAddress = request.SenderAddress;
        account.IsDefault = request.IsDefault;
        account.IsActive = request.IsActive;
        account.AutoCreateLeads = request.AutoCreateLeads;
        account.Host = request.Host;
        account.Port = request.Port;
        account.Username = request.Username;
        account.PhoneNumberId = request.PhoneNumberId;
        account.AccountSid = request.AccountSid;
        account.WebhookSecret = request.WebhookSecret;

        if (!string.IsNullOrEmpty(request.Password))
        {
            account.EncryptedPassword = secretProtector.Protect(request.Password);
        }
        if (!string.IsNullOrEmpty(request.AuthToken))
        {
            account.EncryptedAuthToken = secretProtector.Protect(request.AuthToken);
        }

        await dbContext.SaveChangesAsync(ct);
        return ToDto(account);
    }

    public async Task DeleteAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await dbContext.ChannelAccounts.FindAsync([id], ct);
        if (account != null)
        {
            dbContext.ChannelAccounts.Remove(account);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<TestConnectionResult> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var account = await dbContext.ChannelAccounts.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Channel account {id} not found.");

        var sender = senders.FirstOrDefault(s => s.Channel == account.Channel);
        if (sender == null)
        {
            return new TestConnectionResult(false, $"No sender provider registered for channel {account.Channel}");
        }

        return await sender.TestAsync(account, ct);
    }

    private static ChannelAccountDto ToDto(ChannelAccount a)
    {
        return new ChannelAccountDto(
            a.Id,
            a.Channel,
            a.Name,
            a.SenderAddress,
            a.IsDefault,
            a.IsActive,
            a.AutoCreateLeads,
            a.CreatedAt,
            a.LastSyncedAt,
            a.StatusMessage
        );
    }
}

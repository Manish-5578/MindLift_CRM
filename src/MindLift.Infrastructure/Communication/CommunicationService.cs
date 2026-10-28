using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class CommunicationService(
    AppDbContext dbContext,
    IEnumerable<IChannelSender> channelSenders,
    IOptOutService optOutService,
    IMessageTemplateService templateService,
    ITenantContext tenant,
    ILogger<CommunicationService> logger,
    IDataScopeService scope,
    MindLift.Infrastructure.Files.FileService files) : ICommunicationService
{
    /// <summary>
    /// Messages the caller may read. A message linked to a lead, contact, account or deal follows that record's data scope;
    /// a message linked to nothing is visible to its sender and to people with All scope on Communication.
    /// </summary>
    private async Task<IQueryable<Message>> VisibleMessagesAsync(CancellationToken ct)
    {
        var leads = await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Leads, ct);
        var contacts = await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Contacts, ct);
        var accounts = await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Accounts, ct);
        var deals = await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Deals, ct);
        var comms = await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Communication, ct);

        bool allLeads = leads is null, allContacts = contacts is null, allAccounts = accounts is null, allDeals = deals is null, allComms = comms is null;
        var leadIds = leads?.ToList() ?? [];
        var contactIds = contacts?.ToList() ?? [];
        var accountIds = accounts?.ToList() ?? [];
        var dealIds = deals?.ToList() ?? [];
        var commIds = comms?.ToList() ?? [];

        return dbContext.Messages.Where(m =>
            (m.LeadId != null && (allLeads || dbContext.Leads.Any(l => l.Id == m.LeadId && leadIds.Contains(l.OwnerId))))
            || (m.LeadId == null && m.ContactId != null && (allContacts || dbContext.Contacts.Any(c => c.Id == m.ContactId && contactIds.Contains(c.OwnerId))))
            || (m.LeadId == null && m.ContactId == null && m.AccountId != null && (allAccounts || dbContext.Accounts.Any(a => a.Id == m.AccountId && accountIds.Contains(a.OwnerId))))
            || (m.LeadId == null && m.ContactId == null && m.AccountId == null && m.DealId != null && (allDeals || dbContext.Deals.Any(d => d.Id == m.DealId && dealIds.Contains(d.OwnerId))))
            || (m.LeadId == null && m.ContactId == null && m.AccountId == null && m.DealId == null && (allComms || (m.SentByUserId != null && commIds.Contains(m.SentByUserId.Value)))));
    }

    private async Task EnsureLinkVisibleAsync<T>(Guid? id, IQueryable<T> source, MindLift.Shared.Enums.AppModule module, System.Linq.Expressions.Expression<Func<T, Guid>> owner, System.Linq.Expressions.Expression<Func<T, Guid>> key, string what, CancellationToken ct)
    {
        if (id is not { } wanted)
        {
            return;
        }

        var visible = await MindLift.Infrastructure.Contacts.RecordHelpers.InScopeAsync(source, scope, module, owner, ct);
        var match = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.Equal(key.Body, System.Linq.Expressions.Expression.Constant(wanted)), key.Parameters);
        if (!await visible.AnyAsync(match, ct))
        {
            throw new MindLift.Application.Common.Exceptions.NotFoundException(what, wanted);
        }
    }

    public async Task<MessageDto> SendMessageAsync(SendMessageRequest request, Guid userId, CancellationToken ct = default)
    {
        // 1. Opt-out validation
        if (await optOutService.IsOptedOutAsync(request.To, request.Channel, ct))
        {
            throw new InvalidOperationException($"Recipient '{request.To}' has opted out of communication via {request.Channel}.");
        }

        // Records named by the caller must be ones they can see, and attachments must be files they can read.
        await EnsureLinkVisibleAsync(request.LeadId, dbContext.Leads.AsNoTracking(), MindLift.Shared.Enums.AppModule.Leads, x => x.OwnerId, x => x.Id, "Lead", ct);
        await EnsureLinkVisibleAsync(request.ContactId, dbContext.Contacts.AsNoTracking(), MindLift.Shared.Enums.AppModule.Contacts, x => x.OwnerId, x => x.Id, "Contact", ct);
        await EnsureLinkVisibleAsync(request.AccountId, dbContext.Accounts.AsNoTracking(), MindLift.Shared.Enums.AppModule.Accounts, x => x.OwnerId, x => x.Id, "Account", ct);
        await EnsureLinkVisibleAsync(request.DealId, dbContext.Deals.AsNoTracking(), MindLift.Shared.Enums.AppModule.Deals, x => x.OwnerId, x => x.Id, "Deal", ct);
        if (request.AttachmentFileIds is { Count: > 0 } attachmentIds)
        {
            await files.EnsureReadableAsync(attachmentIds, ct);
        }
        // 2. Resolve Channel Account
        ChannelAccount account;
        if (request.ChannelAccountId.HasValue)
        {
            var requested = await dbContext.ChannelAccounts
                .FirstOrDefaultAsync(a => a.Id == request.ChannelAccountId.Value && a.Channel == request.Channel && a.IsActive, ct);
            if (requested is null)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["channelAccountId"] = ["The requested channel account is invalid, inactive, or configured for a different channel."]
                });
            }

            account = requested;
        }
        else
        {
            var defaultAccount = await dbContext.ChannelAccounts
                .Where(a => a.Channel == request.Channel && a.IsActive)
                .OrderByDescending(a => a.IsDefault)
                .FirstOrDefaultAsync(ct);

            account = defaultAccount ?? new ChannelAccount
            {
                Channel = request.Channel,
                Name = $"{request.Channel} Default",
                SenderAddress = request.Channel == CommunicationChannel.Email ? "noreply@mindlift.com" : "+10000000000",
                IsActive = true
            };
        }

        if (account.Channel != request.Channel || !account.IsActive)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["channel"] = ["Channel account does not match the requested communication channel or is inactive."]
            });
        }

        // 3. Resolve Body (Template or Direct)
        var body = request.Body;
        if (request.TemplateId.HasValue && string.IsNullOrWhiteSpace(body))
        {
            var entityType = request.LeadId.HasValue ? "lead" : request.ContactId.HasValue ? "contact" : request.DealId.HasValue ? "deal" : "";
            var entityId = request.LeadId ?? request.ContactId ?? request.DealId ?? Guid.Empty;
            body = await templateService.RenderTemplateAsync(request.TemplateId.Value, entityType, entityId, request.TemplateParameters, ct);
        }

        // 4. Resolve Lead / Contact association if not provided
        var leadId = request.LeadId;
        var contactId = request.ContactId;
        var accountId = request.AccountId;
        var dealId = request.DealId;

        if (!leadId.HasValue && !contactId.HasValue)
        {
            // Auto-linking by address only looks at records the sender can see.
            var normalizedTarget = OptOutService.NormalizeTarget(request.To, request.Channel);
            var visibleLeads = await MindLift.Infrastructure.Contacts.RecordHelpers.InScopeAsync(dbContext.Leads.AsQueryable(), scope, MindLift.Shared.Enums.AppModule.Leads, l => l.OwnerId, ct);
            var lead = await visibleLeads.FirstOrDefaultAsync(l => l.NormalizedEmail == normalizedTarget || l.NormalizedPhone == normalizedTarget, ct);
            if (lead != null)
            {
                leadId = lead.Id;
            }
            else
            {
                var visibleContacts = await MindLift.Infrastructure.Contacts.RecordHelpers.InScopeAsync(dbContext.Contacts.AsQueryable(), scope, MindLift.Shared.Enums.AppModule.Contacts, c => c.OwnerId, ct);
                var contact = await visibleContacts.FirstOrDefaultAsync(c => c.NormalizedEmail == normalizedTarget || c.NormalizedPhone == normalizedTarget, ct);
                if (contact != null)
                {
                    contactId = contact.Id;
                    accountId ??= contact.AccountId;
                }
            }
        }

        // 5. Thread ID resolution
        var existingMessage = await (await VisibleMessagesAsync(ct))
            .Where(m => m.Channel == request.Channel && (m.To == request.To || m.From == request.To))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var threadId = existingMessage?.ThreadId ?? Guid.NewGuid();

        // 6. Create Message record
        var message = new Message
        {
            Channel = request.Channel,
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sending,
            From = account.SenderAddress,
            To = request.To,
            Subject = request.Subject,
            Body = body,
            ThreadId = threadId,
            ChannelAccountId = account.Id != Guid.Empty ? account.Id : null,
            LeadId = leadId,
            ContactId = contactId,
            AccountId = accountId,
            DealId = dealId,
            SentByUserId = userId
        };

        if (request.AttachmentFileIds != null && request.AttachmentFileIds.Count > 0)
        {
            var files = await dbContext.Files.Where(f => request.AttachmentFileIds.Contains(f.Id)).ToListAsync(ct);
            var attachments = files.Select(f => new MessageAttachmentDto(f.Id, f.FileName, f.SizeBytes, f.ContentType)).ToList();
            message.AttachmentsJson = JsonSerializer.Serialize(attachments);
        }

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(ct);

        // 7. Dispatch to provider
        var sender = channelSenders.FirstOrDefault(s => s.Channel == request.Channel);
        if (sender != null)
        {
            var sendResult = await sender.SendAsync(account, request.To, request.Subject, body, request.AttachmentFileIds, ct);
            if (sendResult.Success)
            {
                message.Status = MessageStatus.Sent;
                message.ExternalMessageId = sendResult.ExternalMessageId;
            }
            else
            {
                message.Status = MessageStatus.Failed;
                message.ErrorMessage = sendResult.ErrorMessage;
            }
        }
        else
        {
            message.Status = MessageStatus.Sent;
            message.ExternalMessageId = $"sim_{Guid.NewGuid():N}";
        }

        // 8. Create record timeline Activity
        try
        {
            var act = new Activity
            {
                Type = request.Channel == CommunicationChannel.Email ? "Note" : "Call",
                Subject = $"{request.Channel} Outbound: {(string.IsNullOrEmpty(request.Subject) ? request.To : request.Subject)}",
                Description = body,
                Status = "Completed",
                CompletedAt = DateTime.UtcNow,
                CompletedById = userId,
                OwnerId = userId,
                LeadId = leadId,
                ContactId = contactId,
                AccountId = accountId,
                DealId = dealId
            };
            dbContext.Activities.Add(act);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create timeline activity for outbound message {MessageId}", message.Id);
        }

        await dbContext.SaveChangesAsync(ct);
        return MapToDto(message, null, null, null);
    }

    public async Task<List<MessageDto>> GetThreadMessagesAsync(Guid threadId, CancellationToken ct = default)
    {
        var messages = await (await VisibleMessagesAsync(ct))
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var leadIds = messages.Where(m => m.LeadId.HasValue).Select(m => m.LeadId!.Value).Distinct().ToList();
        var contactIds = messages.Where(m => m.ContactId.HasValue).Select(m => m.ContactId!.Value).Distinct().ToList();
        var userIds = messages.Where(m => m.SentByUserId.HasValue).Select(m => m.SentByUserId!.Value).Distinct().ToList();

        var leads = await dbContext.Leads.Where(l => leadIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => $"{l.FirstName} {l.LastName}".Trim(), ct);
        var contacts = await dbContext.Contacts.Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim(), ct);
        var users = await dbContext.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        return messages.Select(m => MapToDto(
            m,
            m.LeadId.HasValue && leads.TryGetValue(m.LeadId.Value, out var ln) ? ln : null,
            m.ContactId.HasValue && contacts.TryGetValue(m.ContactId.Value, out var cn) ? cn : null,
            m.SentByUserId.HasValue && users.TryGetValue(m.SentByUserId.Value, out var un) ? un : null
        )).ToList();
    }

    public async Task<List<InboxThreadDto>> GetInboxThreadsAsync(CommunicationChannel? channel = null, string? search = null, CancellationToken ct = default)
    {
        var query = await VisibleMessagesAsync(ct);
        if (channel.HasValue)
        {
            query = query.Where(m => m.Channel == channel.Value);
        }

        var allMessages = await query
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        var grouped = allMessages.GroupBy(m => m.ThreadId);

        var leadIds = allMessages.Where(m => m.LeadId.HasValue).Select(m => m.LeadId!.Value).Distinct().ToList();
        var contactIds = allMessages.Where(m => m.ContactId.HasValue).Select(m => m.ContactId!.Value).Distinct().ToList();

        var leads = await dbContext.Leads.Where(l => leadIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => $"{l.FirstName} {l.LastName}".Trim(), ct);
        var contacts = await dbContext.Contacts.Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim(), ct);

        var results = new List<InboxThreadDto>();
        foreach (var group in grouped)
        {
            var latest = group.First();
            var unreadCount = group.Count(m => m.Direction == MessageDirection.Inbound && m.ReadAt == null);

            var participantAddress = latest.Direction == MessageDirection.Inbound ? latest.From : latest.To;
            string? participantName = null;

            if (latest.LeadId.HasValue && leads.TryGetValue(latest.LeadId.Value, out var ln))
                participantName = ln;
            else if (latest.ContactId.HasValue && contacts.TryGetValue(latest.ContactId.Value, out var cn))
                participantName = cn;

            var snippet = latest.Body.Length > 80 ? latest.Body[..80] + "..." : latest.Body;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var match = participantAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            (participantName != null && participantName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                            snippet.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
            }

            results.Add(new InboxThreadDto(
                group.Key,
                latest.Channel,
                participantAddress,
                participantName,
                latest.LeadId,
                latest.ContactId,
                snippet,
                latest.CreatedAt,
                unreadCount,
                latest.Status
            ));
        }

        return results.OrderByDescending(t => t.LastMessageAt).ToList();
    }

    public async Task<List<MessageDto>> GetRecordMessagesAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var type = entityType.Trim().ToLowerInvariant();
        IQueryable<Message> query = await VisibleMessagesAsync(ct);

        if (type is "lead" or "leads") query = query.Where(m => m.LeadId == entityId);
        else if (type is "contact" or "contacts") query = query.Where(m => m.ContactId == entityId);
        else if (type is "account" or "accounts") query = query.Where(m => m.AccountId == entityId);
        else if (type is "deal" or "deals") query = query.Where(m => m.DealId == entityId);
        else return [];

        var list = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
        return list.Select(m => MapToDto(m, null, null, null)).ToList();
    }

    public async Task MarkThreadReadAsync(Guid threadId, CancellationToken ct = default)
    {
        var unread = await (await VisibleMessagesAsync(ct))
            .Where(m => m.ThreadId == threadId && m.Direction == MessageDirection.Inbound && m.ReadAt == null)
            .ToListAsync(ct);

        foreach (var msg in unread)
        {
            msg.ReadAt = DateTime.UtcNow;
            msg.Status = MessageStatus.Read;
        }

        if (unread.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task HandleInboundMessageAsync(InboundMessagePayload payload, Guid? channelAccountId = null, CancellationToken ct = default)
    {
        var cleanTo = payload.To.Trim().TrimStart('+');
        // 1. Find the channel account across all tenants. A verified webhook names it, so the body cannot redirect the message to another workspace.
        var account = channelAccountId is { } verifiedId
            ? await dbContext.ChannelAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == verifiedId && a.Channel == payload.Channel, ct)
            : await dbContext.ChannelAccounts.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Channel == payload.Channel && (a.SenderAddress == payload.To || a.PhoneNumberId == payload.To || a.PhoneNumberId == cleanTo || a.SenderAddress == cleanTo), ct);

        if (account == null)
        {
            logger.LogWarning("Inbound message received for unknown channel account: {To}", payload.To);
            return;
        }

        var owner = await dbContext.Users.IgnoreQueryFilters().Where(u => u.TenantId == account.TenantId).OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        using var scope = tenant.Use(account.TenantId, owner?.Id);

        // 2. Resolve matching Lead or Contact
        var normalizedSender = OptOutService.NormalizeTarget(payload.From, payload.Channel);
        var lead = await dbContext.Leads.FirstOrDefaultAsync(l => l.NormalizedEmail == normalizedSender || l.NormalizedPhone == normalizedSender, ct);
        var contact = lead == null ? await dbContext.Contacts.FirstOrDefaultAsync(c => c.NormalizedEmail == normalizedSender || c.NormalizedPhone == normalizedSender, ct) : null;

        Guid? leadId = lead?.Id;
        Guid? contactId = contact?.Id;
        Guid? accountId = contact?.AccountId;

        // Auto-create lead if enabled and no entity found
        if (leadId == null && contactId == null && account.AutoCreateLeads)
        {
            var newLead = new Lead
            {
                FirstName = "Inbound",
                LastName = payload.From,
                Email = payload.Channel == CommunicationChannel.Email ? payload.From : null,
                Phone = payload.Channel != CommunicationChannel.Email ? payload.From : null,
                NormalizedEmail = payload.Channel == CommunicationChannel.Email ? normalizedSender : null,
                NormalizedPhone = payload.Channel != CommunicationChannel.Email ? normalizedSender : null,
                Status = "new",
                Source = payload.Channel.ToString(),
                OwnerId = owner?.Id ?? Guid.Empty
            };
            dbContext.Leads.Add(newLead);
            await dbContext.SaveChangesAsync(ct);
            leadId = newLead.Id;
        }

        // 3. Resolve ThreadId
        var existingMessage = await dbContext.Messages
            .Where(m => m.Channel == payload.Channel && (m.From == payload.From || m.To == payload.From))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var threadId = existingMessage?.ThreadId ?? Guid.NewGuid();

        // 4. Save Message
        var message = new Message
        {
            Channel = payload.Channel,
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered,
            From = payload.From,
            To = payload.To,
            Subject = payload.Subject,
            Body = payload.Body,
            ExternalMessageId = payload.ExternalMessageId,
            DeliveredAt = payload.ReceivedAt,
            ThreadId = threadId,
            ChannelAccountId = account.Id,
            LeadId = leadId,
            ContactId = contactId,
            AccountId = accountId
        };

        if (payload.Attachments != null && payload.Attachments.Count > 0)
        {
            message.AttachmentsJson = JsonSerializer.Serialize(payload.Attachments);
        }

        dbContext.Messages.Add(message);

        // 5. Add timeline activity
        var act = new Activity
        {
            Type = payload.Channel == CommunicationChannel.Email ? "Note" : "Call",
            Subject = $"{payload.Channel} Inbound: {payload.From}",
            Description = payload.Body,
            Status = "Completed",
            CompletedAt = DateTime.UtcNow,
            LeadId = leadId,
            ContactId = contactId,
            AccountId = accountId
        };
        dbContext.Activities.Add(act);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateMessageStatusAsync(string externalMessageId, MessageStatus status, DateTime? timestamp = null, string? error = null, Guid? tenantId = null, Guid? channelAccountId = null, CancellationToken ct = default)
    {
        // A verified webhook names its channel account; only that account's messages may change.
        var message = await dbContext.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ExternalMessageId == externalMessageId
                && (tenantId == null || m.TenantId == tenantId)
                && (channelAccountId == null || m.ChannelAccountId == channelAccountId), ct);

        if (message == null) return;

        using var scope = tenant.Use(message.TenantId);

        message.Status = status;
        if (status == MessageStatus.Delivered && !message.DeliveredAt.HasValue)
        {
            message.DeliveredAt = timestamp ?? DateTime.UtcNow;
        }
        else if (status == MessageStatus.Read && !message.ReadAt.HasValue)
        {
            message.ReadAt = timestamp ?? DateTime.UtcNow;
        }

        if (!string.IsNullOrEmpty(error))
        {
            message.ErrorMessage = error;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static MessageDto MapToDto(Message m, string? leadName, string? contactName, string? userName)
    {
        List<MessageAttachmentDto>? attachments = null;
        if (!string.IsNullOrWhiteSpace(m.AttachmentsJson) && m.AttachmentsJson != "[]")
        {
            try
            {
                attachments = JsonSerializer.Deserialize<List<MessageAttachmentDto>>(m.AttachmentsJson);
            }
            catch { }
        }

        return new MessageDto(
            m.Id,
            m.Channel,
            m.Direction,
            m.Status,
            m.From,
            m.To,
            m.Subject,
            m.Body,
            m.ErrorMessage,
            m.ThreadId,
            m.ChannelAccountId,
            m.LeadId,
            leadName,
            m.ContactId,
            contactName,
            m.AccountId,
            m.DealId,
            m.SentByUserId,
            userName,
            m.CreatedAt,
            m.DeliveredAt,
            m.ReadAt,
            m.ExternalMessageId,
            attachments
        );
    }
}

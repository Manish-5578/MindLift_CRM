using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 5 / #29: Two-way email sync (email-sync).
/// Service managing connected user mailboxes, bi-directional email syncing,
/// automatic CRM linking (leads, contacts, deals), and outbound CRM email dispatch.
/// </summary>
public interface IEmailSyncService
{
    Task<IReadOnlyList<EmailSyncAccountDto>> ListAccountsAsync(CancellationToken ct = default);
    Task<EmailSyncAccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default);
    Task<EmailSyncAccountDto> CreateAccountAsync(CreateEmailSyncAccountRequest request, CancellationToken ct = default);
    Task<EmailSyncAccountDto> UpdateAccountAsync(Guid id, UpdateEmailSyncAccountRequest request, CancellationToken ct = default);
    Task<bool> DeleteAccountAsync(Guid id, CancellationToken ct = default);
    Task<TestMailboxConnectionResult> TestConnectionAsync(TestMailboxConnectionRequest request, CancellationToken ct = default);
    Task<EmailSyncResultDto> SyncAccountAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EmailSyncMessageDto>> ListMessagesAsync(Guid? accountId, string? entityType, Guid? entityId, int page, int pageSize, CancellationToken ct = default);
    Task<EmailSyncMessageDto?> GetMessageAsync(Guid id, CancellationToken ct = default);
    Task<EmailSyncMessageDto> SendEmailAsync(Guid accountId, SendSyncEmailRequest request, CancellationToken ct = default);
    Task<EmailSyncMessageDto> IngestInboundAsync(Guid accountId, SimulateInboundEmailRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<EmailSyncLogDto>> ListLogsAsync(Guid? accountId, int count = 50, CancellationToken ct = default);
    Task<EmailSyncStatsDto> GetStatsAsync(CancellationToken ct = default);
}

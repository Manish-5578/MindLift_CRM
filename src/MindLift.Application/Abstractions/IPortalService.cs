using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IPortalService
{
    // Anonymous Branding & Auth
    Task<PortalBrandingDto> GetBrandingAsync(string tenantSlug, CancellationToken ct = default);
    Task<PortalLoginResponse> LoginAsync(string tenantSlug, PortalLoginRequest req, CancellationToken ct = default);
    Task SendMagicLinkAsync(string tenantSlug, PortalMagicLinkRequest req, CancellationToken ct = default);
    Task<PortalLoginResponse> VerifyMagicLinkAsync(string tenantSlug, PortalMagicLinkVerifyRequest req, CancellationToken ct = default);
    Task<PortalLoginResponse> AcceptInviteAsync(string tenantSlug, AcceptPortalInviteRequest req, CancellationToken ct = default);
    Task<PortalLoginResponse> RefreshAsync(string tenantSlug, string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string tenantSlug, string refreshToken, CancellationToken ct = default);
    Task<PortalProfileDto> GetProfileAsync(Guid portalUserId, CancellationToken ct = default);

    /// <summary>Throws unless the portal module is entitled and switched on, the company is not suspended, and the portal user is still active.</summary>
    Task EnsureCustomerAccessAsync(Guid portalUserId, CancellationToken ct = default);
    Task<PortalProfileDto> UpdateProfileAsync(Guid portalUserId, UpdatePortalProfileRequest req, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid portalUserId, ChangePortalPasswordRequest req, CancellationToken ct = default);

    // Customer Portal Authenticated
    Task<PortalHomeDto> GetHomeAsync(Guid portalUserId, Guid contactId, Guid? accountId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDocumentDto>> GetCustomerDocumentsAsync(Guid contactId, Guid? accountId, string? category, string? search, CancellationToken ct = default);
    Task<IReadOnlyList<PortalDocumentRequestDto>> GetCustomerRequestsAsync(Guid contactId, CancellationToken ct = default);
    Task<BusinessDocumentDto> SubmitDocumentRequestAsync(Guid contactId, Guid? accountId, Guid requestId, string fileName, string contentType, long size, byte[] bytes, CancellationToken ct = default);

    // Staff Management
    Task<PortalSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<PortalSettingsDto> SaveSettingsAsync(SavePortalSettingsRequest req, CancellationToken ct = default);
    Task InviteContactAsync(InvitePortalUserRequest req, CancellationToken ct = default);
    Task<PortalAccessStatusDto> GetContactAccessAsync(Guid contactId, CancellationToken ct = default);
    Task<IReadOnlyList<PortalDocumentRequestDto>> GetDocumentRequestsForRecordAsync(Guid? contactId, Guid? accountId, Guid? dealId, CancellationToken ct = default);
    Task<PortalDocumentRequestDto> CreateDocumentRequestAsync(SaveDocumentRequest req, CancellationToken ct = default);
    Task<PortalDocumentRequestDto> UpdateDocumentRequestStatusAsync(Guid id, string status, CancellationToken ct = default);
    Task SetDocumentSharingAsync(Guid documentId, bool shared, CancellationToken ct = default);
}

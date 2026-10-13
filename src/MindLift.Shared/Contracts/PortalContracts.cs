namespace MindLift.Shared.Contracts;

public sealed record PortalSettingsDto(
    bool IsEnabled,
    string? WelcomeMessage,
    string? WelcomeMessageAr,
    bool ShowMetrics,
    bool AllowGeneralUploads,
    bool NotifyCustomerOnExpiry,
    string? SupportEmail,
    string? SupportPhone
);

public sealed record SavePortalSettingsRequest(
    bool IsEnabled,
    string? WelcomeMessage,
    string? WelcomeMessageAr,
    bool ShowMetrics,
    bool AllowGeneralUploads,
    bool NotifyCustomerOnExpiry,
    string? SupportEmail,
    string? SupportPhone
);

public sealed record PortalBrandingDto(
    string TenantName,
    string? LogoUrl,
    string PrimaryColor,
    string DefaultLanguage,
    string? SupportEmail,
    string? SupportPhone,
    string? WelcomeMessage,
    string? WelcomeMessageAr,
    bool Enabled
);

public sealed record PortalLoginRequest(
    string Email,
    string Password
);

public sealed record PortalLoginResponse(
    string AccessToken,
    Guid PortalUserId,
    Guid ContactId,
    Guid? AccountId,
    string CustomerName,
    string? AccountName,
    string Language,
    DateTime? ExpiresAtUtc = null,
    string? RefreshToken = null
);

/// <summary>Body form of the portal refresh token, used by mobile clients. Browsers send the HttpOnly cookie.</summary>
public sealed record PortalRefreshRequest(string? RefreshToken);

/// <summary>The signed-in customer's own profile.</summary>
public sealed record PortalProfileDto(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? AccountName,
    string Language,
    DateTime? LastLoginAt
);

public sealed record UpdatePortalProfileRequest(string Language);

public sealed record ChangePortalPasswordRequest(string CurrentPassword, string NewPassword);

public sealed record PortalMagicLinkRequest(
    string Email
);

public sealed record PortalMagicLinkVerifyRequest(
    string Token
);

public sealed record InvitePortalUserRequest(
    Guid ContactId,
    string Email
);

public sealed record AcceptPortalInviteRequest(
    string Token,
    string Password
);

public sealed record PortalDocumentRequestDto(
    Guid Id,
    Guid ContactId,
    Guid? AccountId,
    Guid? DealId,
    string Title,
    string? Description,
    DateOnly? DueDate,
    string Status,
    DateTime CreatedAt
);

public sealed record SaveDocumentRequest(
    Guid ContactId,
    Guid? AccountId,
    Guid? DealId,
    string Title,
    string? Description,
    DateOnly? DueDate
);

/// <summary>Staff decision on a document request: Completed (accepted) or Cancelled.</summary>
public sealed record UpdateDocumentRequestStatusRequest(string Status);

/// <summary>Portal access for one contact. Status is NotInvited, Invited, Active or Disabled.</summary>
public sealed record PortalAccessStatusDto(
    Guid ContactId,
    string Status,
    string? Email,
    DateTime? InvitedAt,
    DateTime? ActivatedAt,
    DateTime? LastLoginAt
);

public sealed record SetDocumentSharingRequest(bool SharedWithPortal);

public sealed record PortalKeyInfoFieldDto(
    string Label,
    string Value,
    string ValueType,
    bool IsSensitive
);

public sealed record PortalKeyInfoDto(
    string TypeKey,
    string TypeName,
    Guid DocumentId,
    DateOnly? ExpiryDate,
    int? DaysLeft,
    string Validity,
    List<PortalKeyInfoFieldDto> Fields
);

public sealed record PortalMetricPointDto(
    string Period,
    decimal Value
);

public sealed record PortalMetricCardDto(
    string Key,
    string Label,
    string Currency,
    decimal Latest,
    decimal? Previous,
    decimal? ChangePct,
    List<PortalMetricPointDto> Series
);

public sealed record PortalHomeDto(
    string CustomerName,
    string? AccountName,
    string? WelcomeMessage,
    List<PortalDocumentRequestDto> OpenRequests,
    List<PortalKeyInfoDto> KeyInfo,
    List<PortalMetricCardDto> Metrics,
    List<BusinessDocumentDto> RecentDocuments
);

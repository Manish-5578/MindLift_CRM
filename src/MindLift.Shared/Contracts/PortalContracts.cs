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
    string Language
);

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

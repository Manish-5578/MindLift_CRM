using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 4 / #27: GDPR and unsubscribe center (unsubscribe-center).
/// Service managing GDPR Article 7 consent tracking, self-service subscription preferences,
/// RFC 8058 one-click unsubscribe, Article 15/20 data portability, and Article 17 erasure.
/// </summary>
public interface IGdprConsentService
{
    Task<PreferenceCenterDetailsDto?> GetPreferenceCenterAsync(string token, CancellationToken ct = default);

    Task<bool> UpdatePreferencesAsync(
        string token,
        UpdatePreferencesRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default);

    Task<bool> ProcessOneClickUnsubscribeAsync(
        string token,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default);

    Task<PreferenceTokenDto> GeneratePreferenceTokenAsync(
        GeneratePreferenceTokenRequest request,
        CancellationToken ct = default);

    Task<ConsentCheckResult> CheckConsentAsync(
        string email,
        CommunicationChannel channel = CommunicationChannel.Email,
        string topic = "Marketing",
        CancellationToken ct = default);

    Task<IReadOnlyList<GdprConsentRecordDto>> ListConsentRecordsAsync(
        string? email = null,
        CommunicationChannel? channel = null,
        string? topic = null,
        ConsentStatus? status = null,
        CancellationToken ct = default);

    Task<GdprConsentRecordDto> SaveConsentRecordAsync(
        SaveConsentRecordRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken ct = default);

    Task<GdprSubjectDataExport> ExportSubjectDataAsync(string email, CancellationToken ct = default);

    Task<GdprErasureResult> ExecuteErasureAsync(GdprErasureRequest request, CancellationToken ct = default);
}

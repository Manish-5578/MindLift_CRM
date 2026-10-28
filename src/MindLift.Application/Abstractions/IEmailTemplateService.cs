using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 4 / #26: Visual email template builder (email-template-builder).
/// Service for managing email templates, compiling visual layouts, resolving merge tags,
/// and live rendering against CRM records.
/// </summary>
public interface IEmailTemplateService
{
    Task<IReadOnlyList<EmailTemplateSummaryDto>> ListTemplatesAsync(
        string? category = null,
        string? search = null,
        bool? activeOnly = null,
        CancellationToken ct = default);

    Task<EmailTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);

    Task<EmailTemplateDto> CreateTemplateAsync(SaveEmailTemplateRequest request, CancellationToken ct = default);

    Task<EmailTemplateDto?> UpdateTemplateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken ct = default);

    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct = default);

    Task<EmailTemplateDto?> CloneTemplateAsync(Guid id, CloneEmailTemplateRequest request, CancellationToken ct = default);

    Task<RenderedEmailResult> RenderPreviewAsync(RenderTemplateRequest request, CancellationToken ct = default);

    Task<RenderedEmailResult?> RenderTemplateWithEntityAsync(
        Guid templateId,
        RenderWithEntityRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<EmailTemplatePresetDto>> GetPresetsAsync(CancellationToken ct = default);

    Task<EmailTemplateDto> CreateFromPresetAsync(
        string presetKey,
        string? customName = null,
        CancellationToken ct = default);
}

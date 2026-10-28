using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #26: Visual email template builder (email-template-builder).
/// Provides visual block layout compilation, responsive email HTML generation,
/// plain-text fallback generation, merge tag extraction and entity-bound rendering.
/// </summary>
public sealed class EmailTemplateService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ILogger<EmailTemplateService> logger) : IEmailTemplateService
{
    private static readonly Regex MergeTagRegex = new(@"\{\{([a-zA-Z0-9_\.]+)\}\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

    public async Task<IReadOnlyList<EmailTemplateSummaryDto>> ListTemplatesAsync(
        string? category = null,
        string? search = null,
        bool? activeOnly = null,
        CancellationToken ct = default)
    {
        var query = db.Set<EmailTemplate>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(t => t.Category.ToLower() == cat.ToLower());
        }

        if (activeOnly == true)
        {
            query = query.Where(t => t.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Subject.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        var list = await query
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Select(t => new EmailTemplateSummaryDto
            {
                Id = t.Id,
                Name = t.Name,
                Subject = t.Subject,
                Category = t.Category,
                Description = t.Description,
                MergeTags = t.MergeTags,
                IsActive = t.IsActive,
                IsPreset = t.IsPreset,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync(ct);

        return list;
    }

    public async Task<EmailTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.Set<EmailTemplate>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (template == null) return null;

        return MapToDto(template);
    }

    public async Task<EmailTemplateDto> CreateTemplateAsync(SaveEmailTemplateRequest request, CancellationToken ct = default)
    {
        var blocks = request.Blocks;
        string layoutJson;

        if (blocks != null && blocks.Count > 0)
        {
            layoutJson = JsonSerializer.Serialize(blocks, JsonOptions);
        }
        else if (!string.IsNullOrWhiteSpace(request.LayoutJson))
        {
            layoutJson = request.LayoutJson;
            blocks = TryParseBlocks(layoutJson);
        }
        else
        {
            blocks = [];
            layoutJson = "[]";
        }

        string renderedHtml;
        string plainText;

        if (!string.IsNullOrWhiteSpace(request.CustomHtml))
        {
            renderedHtml = request.CustomHtml;
            plainText = !string.IsNullOrWhiteSpace(request.CustomPlainText)
                ? request.CustomPlainText
                : StripHtmlToPlainText(request.CustomHtml);
        }
        else
        {
            renderedHtml = CompileBlocksToHtml(blocks, request.PreheaderText);
            plainText = CompileBlocksToPlainText(blocks);
        }

        var mergeTags = ExtractMergeTags(request.Subject, request.PreheaderText, renderedHtml, plainText);

        var entity = new EmailTemplate
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Untitled Template" : request.Name.Trim(),
            Subject = request.Subject?.Trim() ?? string.Empty,
            PreheaderText = request.PreheaderText?.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
            Description = request.Description?.Trim(),
            LayoutJson = layoutJson,
            RenderedHtml = renderedHtml,
            PlainTextBody = plainText,
            MergeTags = mergeTags,
            ThumbnailUrl = request.ThumbnailUrl,
            IsActive = request.IsActive,
            IsPreset = false
        };

        db.Set<EmailTemplate>().Add(entity);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created EmailTemplate {TemplateId} ('{TemplateName}') with {TagCount} merge tags.",
            entity.Id, entity.Name, entity.MergeTags.Length);

        return MapToDto(entity, blocks);
    }

    public async Task<EmailTemplateDto?> UpdateTemplateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken ct = default)
    {
        var template = await db.Set<EmailTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template == null) return null;

        var blocks = request.Blocks;
        string layoutJson;

        if (blocks != null && blocks.Count > 0)
        {
            layoutJson = JsonSerializer.Serialize(blocks, JsonOptions);
        }
        else if (!string.IsNullOrWhiteSpace(request.LayoutJson))
        {
            layoutJson = request.LayoutJson;
            blocks = TryParseBlocks(layoutJson);
        }
        else if (!string.IsNullOrWhiteSpace(template.LayoutJson) && template.LayoutJson != "[]")
        {
            layoutJson = template.LayoutJson;
            blocks = TryParseBlocks(layoutJson);
        }
        else
        {
            blocks = [];
            layoutJson = "[]";
        }

        string renderedHtml;
        string plainText;

        if (!string.IsNullOrWhiteSpace(request.CustomHtml))
        {
            renderedHtml = request.CustomHtml;
            plainText = !string.IsNullOrWhiteSpace(request.CustomPlainText)
                ? request.CustomPlainText
                : StripHtmlToPlainText(request.CustomHtml);
        }
        else if (blocks.Count > 0)
        {
            renderedHtml = CompileBlocksToHtml(blocks, request.PreheaderText ?? template.PreheaderText);
            plainText = CompileBlocksToPlainText(blocks);
        }
        else
        {
            renderedHtml = template.RenderedHtml;
            plainText = template.PlainTextBody;
        }

        var mergeTags = ExtractMergeTags(request.Subject, request.PreheaderText, renderedHtml, plainText);

        template.Name = string.IsNullOrWhiteSpace(request.Name) ? template.Name : request.Name.Trim();
        template.Subject = request.Subject?.Trim() ?? template.Subject;
        template.PreheaderText = request.PreheaderText?.Trim();
        template.Category = string.IsNullOrWhiteSpace(request.Category) ? template.Category : request.Category.Trim();
        template.Description = request.Description?.Trim();
        template.LayoutJson = layoutJson;
        template.RenderedHtml = renderedHtml;
        template.PlainTextBody = plainText;
        template.MergeTags = mergeTags;
        template.ThumbnailUrl = request.ThumbnailUrl ?? template.ThumbnailUrl;
        template.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Updated EmailTemplate {TemplateId} ('{TemplateName}').", template.Id, template.Name);

        return MapToDto(template, blocks);
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.Set<EmailTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template == null) return false;

        template.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Soft deleted EmailTemplate {TemplateId}.", id);
        return true;
    }

    public async Task<EmailTemplateDto?> CloneTemplateAsync(Guid id, CloneEmailTemplateRequest request, CancellationToken ct = default)
    {
        var source = await db.Set<EmailTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (source == null) return null;

        var newName = !string.IsNullOrWhiteSpace(request.NewName)
            ? request.NewName.Trim()
            : $"{source.Name} (Copy)";

        var clone = new EmailTemplate
        {
            Name = newName,
            Subject = source.Subject,
            PreheaderText = source.PreheaderText,
            Category = source.Category,
            Description = source.Description,
            LayoutJson = source.LayoutJson,
            RenderedHtml = source.RenderedHtml,
            PlainTextBody = source.PlainTextBody,
            MergeTags = (string[])source.MergeTags.Clone(),
            ThumbnailUrl = source.ThumbnailUrl,
            IsActive = true,
            IsPreset = false
        };

        db.Set<EmailTemplate>().Add(clone);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cloned EmailTemplate {SourceId} to new template {NewId} ('{NewName}').",
            id, clone.Id, clone.Name);

        return MapToDto(clone);
    }

    public async Task<RenderedEmailResult> RenderPreviewAsync(RenderTemplateRequest request, CancellationToken ct = default)
    {
        string rawSubject = request.Subject ?? string.Empty;
        string rawHtml = request.HtmlContent ?? string.Empty;
        string rawText = request.PlainTextContent ?? string.Empty;

        if (request.TemplateId.HasValue)
        {
            var template = await db.Set<EmailTemplate>()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TemplateId.Value, ct);

            if (template != null)
            {
                if (string.IsNullOrWhiteSpace(rawSubject)) rawSubject = template.Subject;
                if (string.IsNullOrWhiteSpace(rawHtml)) rawHtml = template.RenderedHtml;
                if (string.IsNullOrWhiteSpace(rawText)) rawText = template.PlainTextBody;
            }
        }
        else if (request.Blocks != null && request.Blocks.Count > 0)
        {
            rawHtml = CompileBlocksToHtml(request.Blocks, null);
            rawText = CompileBlocksToPlainText(request.Blocks);
        }
        else if (!string.IsNullOrWhiteSpace(request.LayoutJson))
        {
            var blocks = TryParseBlocks(request.LayoutJson);
            rawHtml = CompileBlocksToHtml(blocks, null);
            rawText = CompileBlocksToPlainText(blocks);
        }

        var detectedTags = ExtractMergeTags(rawSubject, rawHtml, rawText);
        var variables = new Dictionary<string, string>(request.Variables ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        // Add standard system fallbacks
        if (!variables.ContainsKey("currentDate")) variables["currentDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!variables.ContainsKey("currentYear")) variables["currentYear"] = DateTime.UtcNow.Year.ToString();
        if (!variables.ContainsKey("unsubscribeUrl")) variables["unsubscribeUrl"] = "#unsubscribe";

        var (mergedSubject, missingSubj) = ReplaceTokens(rawSubject, variables);
        var (mergedHtml, missingHtml) = ReplaceTokens(rawHtml, variables);
        var (mergedText, missingText) = ReplaceTokens(rawText, variables);

        var missingVariables = missingSubj
            .Union(missingHtml)
            .Union(missingText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RenderedEmailResult
        {
            Subject = mergedSubject,
            HtmlBody = mergedHtml,
            PlainTextBody = mergedText,
            DetectedMergeTags = detectedTags,
            MissingVariables = missingVariables,
            ResolvedVariables = variables
        };
    }

    public async Task<RenderedEmailResult?> RenderTemplateWithEntityAsync(
        Guid templateId,
        RenderWithEntityRequest request,
        CancellationToken ct = default)
    {
        var template = await db.Set<EmailTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template == null) return null;

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Standard CRM environment variables
        variables["currentDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
        variables["currentYear"] = DateTime.UtcNow.Year.ToString();
        variables["unsubscribeUrl"] = $"https://crm.mindlift.io/unsubscribe?token={Guid.NewGuid():N}&tenant={template.TenantId}";

        var type = request.EntityType.Trim().ToLowerInvariant();
        if (type == "lead")
        {
            var lead = await db.Set<Lead>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.EntityId, ct);
            if (lead != null)
            {
                variables["firstName"] = lead.FirstName;
                variables["lastName"] = lead.LastName;
                variables["fullName"] = $"{lead.FirstName} {lead.LastName}".Trim();
                variables["email"] = lead.Email ?? string.Empty;
                variables["phone"] = lead.Phone ?? string.Empty;
                variables["company"] = lead.CompanyName ?? string.Empty;
                variables["companyName"] = lead.CompanyName ?? string.Empty;
                variables["status"] = lead.Status;
                variables["source"] = lead.Source ?? string.Empty;
                variables["leadSource"] = lead.Source ?? string.Empty;
                variables["score"] = lead.Score.ToString();
                variables["scoreGrade"] = lead.ScoreGrade ?? string.Empty;

                // Prefixed variants for template flexibility
                variables["lead.firstName"] = lead.FirstName;
                variables["lead.lastName"] = lead.LastName;
                variables["lead.company"] = lead.CompanyName ?? string.Empty;
                variables["lead.email"] = lead.Email ?? string.Empty;
            }
        }
        else if (type == "contact")
        {
            var contact = await db.Set<Contact>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.EntityId, ct);
            if (contact != null)
            {
                variables["firstName"] = contact.FirstName;
                variables["lastName"] = contact.LastName;
                variables["fullName"] = $"{contact.FirstName} {contact.LastName}".Trim();
                variables["email"] = contact.Email ?? string.Empty;
                variables["phone"] = contact.Phone ?? string.Empty;
                variables["title"] = contact.JobTitle ?? string.Empty;
                variables["jobTitle"] = contact.JobTitle ?? string.Empty;

                variables["contact.firstName"] = contact.FirstName;
                variables["contact.lastName"] = contact.LastName;
                variables["contact.email"] = contact.Email ?? string.Empty;
            }
        }
        else if (type == "deal")
        {
            var deal = await db.Set<Deal>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.EntityId, ct);
            if (deal != null)
            {
                variables["dealName"] = deal.Name;
                variables["amount"] = deal.Amount.ToString("N2");
                variables["status"] = deal.Status;
                variables["currency"] = deal.Currency;
                variables["expectedCloseDate"] = deal.ExpectedCloseDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                variables["deal.name"] = deal.Name;
                variables["deal.amount"] = deal.Amount.ToString("N2");
            }
        }

        // Apply custom overrides
        if (request.CustomVariables != null)
        {
            foreach (var kvp in request.CustomVariables)
            {
                variables[kvp.Key] = kvp.Value;
            }
        }

        return await RenderPreviewAsync(new RenderTemplateRequest
        {
            TemplateId = templateId,
            Variables = variables
        }, ct);
    }

    public Task<IReadOnlyList<EmailTemplatePresetDto>> GetPresetsAsync(CancellationToken ct = default)
    {
        var presets = BuiltInPresets.Select(p => new EmailTemplatePresetDto
        {
            Key = p.Key,
            Name = p.Name,
            Category = p.Category,
            Description = p.Description,
            Subject = p.Subject,
            PreheaderText = p.PreheaderText,
            Blocks = p.Blocks,
            MergeTags = ExtractMergeTags(p.Subject, p.PreheaderText, CompileBlocksToHtml(p.Blocks, p.PreheaderText))
        }).ToList();

        return Task.FromResult<IReadOnlyList<EmailTemplatePresetDto>>(presets);
    }

    public async Task<EmailTemplateDto> CreateFromPresetAsync(
        string presetKey,
        string? customName = null,
        CancellationToken ct = default)
    {
        var preset = BuiltInPresets.FirstOrDefault(p => string.Equals(p.Key, presetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Template preset '{presetKey}' not found.");

        var request = new SaveEmailTemplateRequest
        {
            Name = !string.IsNullOrWhiteSpace(customName) ? customName.Trim() : preset.Name,
            Subject = preset.Subject,
            PreheaderText = preset.PreheaderText,
            Category = preset.Category,
            Description = preset.Description,
            Blocks = preset.Blocks,
            IsActive = true
        };

        var created = await CreateTemplateAsync(request, ct);

        // Mark as preset origin
        var entity = await db.Set<EmailTemplate>().FirstOrDefaultAsync(t => t.Id == created.Id, ct);
        if (entity != null)
        {
            entity.IsPreset = true;
            await db.SaveChangesAsync(ct);
            created.IsPreset = true;
        }

        return created;
    }

    // ================= Visual Layout Compilation Engine =================

    public static string CompileBlocksToHtml(List<EmailBlockDto> blocks, string? preheaderText)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("  <title>Email</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { margin:0; padding:0; background-color:#f4f6f8; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#2d3748; }");
        sb.AppendLine("    table { border-collapse:collapse; mso-table-lspace:0pt; mso-table-rspace:0pt; }");
        sb.AppendLine("    img { border:0; height:auto; line-height:100%; outline:none; text-decoration:none; max-width:100%; }");
        sb.AppendLine("    .container { width:600px; max-width:600px; margin:0 auto; background-color:#ffffff; border-radius:8px; overflow:hidden; }");
        sb.AppendLine("    @media only screen and (max-width: 600px) {");
        sb.AppendLine("      .container { width:100% !important; border-radius:0 !important; }");
        sb.AppendLine("      .col-cell { display:block !important; width:100% !important; box-sizing:border-box !important; }");
        sb.AppendLine("      .inner-pad { padding-left:16px !important; padding-right:16px !important; }");
        sb.AppendLine("    }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body style=\"margin:0; padding:24px 0; background-color:#f4f6f8;\">");

        if (!string.IsNullOrWhiteSpace(preheaderText))
        {
            sb.AppendLine($"  <div style=\"display:none; font-size:1px; color:#f4f6f8; line-height:1px; max-height:0px; max-width:0px; opacity:0; overflow:hidden;\">{WebUtility.HtmlEncode(preheaderText)}</div>");
        }

        sb.AppendLine("  <table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
        sb.AppendLine("    <tr>");
        sb.AppendLine("      <td align=\"center\">");
        sb.AppendLine("        <table class=\"container\" role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:600px; max-width:600px; background-color:#ffffff; border-radius:8px; box-shadow:0 4px 12px rgba(0,0,0,0.06); overflow:hidden;\">");

        foreach (var block in blocks)
        {
            RenderBlockHtml(sb, block);
        }

        sb.AppendLine("        </table>");
        sb.AppendLine("      </td>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static void RenderBlockHtml(StringBuilder sb, EmailBlockDto block)
    {
        var type = block.Type?.ToLowerInvariant() ?? "text";
        var props = block.Properties;

        switch (type)
        {
            case "header":
            {
                var logoUrl = GetStringProp(props, "logoUrl", "https://mindliftcrm.com/logo.png");
                var alt = GetStringProp(props, "altText", "Logo");
                var align = GetStringProp(props, "align", "center");
                var bgColor = GetStringProp(props, "backgroundColor", "#ffffff");
                var paddingY = GetIntProp(props, "paddingVertical", 24);

                sb.AppendLine($"          <tr><td align=\"{align}\" style=\"padding:{paddingY}px 24px; background-color:{bgColor};\"><img src=\"{logoUrl}\" alt=\"{alt}\" style=\"max-height:48px; border:0; display:inline-block;\" /></td></tr>");
                break;
            }

            case "heading":
            {
                var text = GetStringProp(props, "text", "Heading");
                var align = GetStringProp(props, "align", "left");
                var color = GetStringProp(props, "color", "#1a202c");
                var level = GetIntProp(props, "level", 1);
                var fontSize = level switch { 1 => 24, 2 => 20, _ => 18 };

                sb.AppendLine($"          <tr><td align=\"{align}\" class=\"inner-pad\" style=\"padding:16px 24px 8px 24px; text-align:{align};\"><h{level} style=\"margin:0; font-size:{fontSize}px; font-weight:700; color:{color}; line-height:1.3;\">{WebUtility.HtmlEncode(text)}</h{level}></td></tr>");
                break;
            }

            case "text":
            case "paragraph":
            {
                var content = GetStringProp(props, "content", string.Empty);
                var align = GetStringProp(props, "align", "left");
                var color = GetStringProp(props, "color", "#4a5568");
                var fontSize = GetIntProp(props, "fontSize", 15);
                var lineHeight = GetStringProp(props, "lineHeight", "1.6");

                var paragraphs = content.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.None);
                sb.AppendLine($"          <tr><td align=\"{align}\" class=\"inner-pad\" style=\"padding:8px 24px; text-align:{align}; font-size:{fontSize}px; line-height:{lineHeight}; color:{color};\">");
                foreach (var p in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var encoded = PreserveMergeTags(p.Trim());
                    sb.AppendLine($"            <p style=\"margin:0 0 12px 0;\">{encoded}</p>");
                }
                sb.AppendLine("          </td></tr>");
                break;
            }

            case "button":
            case "cta":
            {
                var label = GetStringProp(props, "label", "Click Here");
                var url = GetStringProp(props, "url", "#");
                var align = GetStringProp(props, "align", "center");
                var bgColor = GetStringProp(props, "backgroundColor", "#3182ce");
                var textColor = GetStringProp(props, "textColor", "#ffffff");
                var radius = GetIntProp(props, "borderRadius", 6);

                sb.AppendLine($"          <tr><td align=\"{align}\" class=\"inner-pad\" style=\"padding:20px 24px;\">");
                sb.AppendLine("            <table role=\"presentation\" border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"display:inline-block;\">");
                sb.AppendLine($"              <tr><td align=\"center\" bgcolor=\"{bgColor}\" style=\"border-radius:{radius}px;\">");
                sb.AppendLine($"                <a href=\"{url}\" style=\"font-size:15px; font-weight:600; color:{textColor}; text-decoration:none; padding:12px 28px; border-radius:{radius}px; display:inline-block; border:1px solid {bgColor};\">{PreserveMergeTags(label)}</a>");
                sb.AppendLine("              </td></tr>");
                sb.AppendLine("            </table>");
                sb.AppendLine("          </td></tr>");
                break;
            }

            case "image":
            {
                var src = GetStringProp(props, "src", "https://via.placeholder.com/600x200");
                var alt = GetStringProp(props, "alt", string.Empty);
                var linkUrl = GetStringProp(props, "linkUrl", string.Empty);
                var align = GetStringProp(props, "align", "center");
                var maxWidth = GetIntProp(props, "maxWidth", 552);
                var radius = GetIntProp(props, "borderRadius", 6);

                sb.AppendLine($"          <tr><td align=\"{align}\" class=\"inner-pad\" style=\"padding:12px 24px;\">");
                if (!string.IsNullOrWhiteSpace(linkUrl))
                {
                    sb.AppendLine($"            <a href=\"{linkUrl}\" target=\"_blank\"><img src=\"{src}\" alt=\"{alt}\" style=\"max-width:{maxWidth}px; width:100%; border-radius:{radius}px; display:block; margin:0 auto;\" /></a>");
                }
                else
                {
                    sb.AppendLine($"            <img src=\"{src}\" alt=\"{alt}\" style=\"max-width:{maxWidth}px; width:100%; border-radius:{radius}px; display:block; margin:0 auto;\" />");
                }
                sb.AppendLine("          </td></tr>");
                break;
            }

            case "columns":
            case "two_column":
            {
                var leftContent = GetStringProp(props, "leftContent", string.Empty);
                var rightContent = GetStringProp(props, "rightContent", string.Empty);

                sb.AppendLine("          <tr><td class=\"inner-pad\" style=\"padding:12px 24px;\">");
                sb.AppendLine("            <table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
                sb.AppendLine("              <tr>");
                sb.AppendLine($"                <td class=\"col-cell\" width=\"50%\" valign=\"top\" style=\"padding:8px; font-size:14px; line-height:1.5; color:#4a5568;\">{PreserveMergeTags(leftContent)}</td>");
                sb.AppendLine($"                <td class=\"col-cell\" width=\"50%\" valign=\"top\" style=\"padding:8px; font-size:14px; line-height:1.5; color:#4a5568;\">{PreserveMergeTags(rightContent)}</td>");
                sb.AppendLine("              </tr>");
                sb.AppendLine("            </table>");
                sb.AppendLine("          </td></tr>");
                break;
            }

            case "divider":
            {
                var color = GetStringProp(props, "color", "#e2e8f0");
                var thickness = GetIntProp(props, "thickness", 1);
                sb.AppendLine($"          <tr><td class=\"inner-pad\" style=\"padding:16px 24px;\"><hr style=\"border:none; border-top:{thickness}px solid {color}; margin:0;\" /></td></tr>");
                break;
            }

            case "spacer":
            {
                var height = GetIntProp(props, "height", 24);
                sb.AppendLine($"          <tr><td height=\"{height}\" style=\"height:{height}px; line-height:{height}px; font-size:1px;\">&nbsp;</td></tr>");
                break;
            }

            case "footer":
            {
                var companyName = GetStringProp(props, "companyName", "MindLift CRM");
                var address = GetStringProp(props, "address", "100 Innovation Way, Suite 400");
                var unsubscribeText = GetStringProp(props, "unsubscribeText", "Unsubscribe from marketing emails");
                var showUnsubscribe = GetBoolProp(props, "showUnsubscribe", true);

                sb.AppendLine("          <tr><td class=\"inner-pad\" style=\"padding:24px; background-color:#f7fafc; border-top:1px solid #edf2f7; text-align:center; font-size:12px; line-height:1.5; color:#718096;\">");
                sb.AppendLine($"            <p style=\"margin:0 0 6px 0; font-weight:600;\">{WebUtility.HtmlEncode(companyName)}</p>");
                sb.AppendLine($"            <p style=\"margin:0 0 10px 0;\">{WebUtility.HtmlEncode(address)}</p>");
                if (showUnsubscribe)
                {
                    sb.AppendLine($"            <p style=\"margin:0;\"><a href=\"{{{{unsubscribeUrl}}}}\" style=\"color:#4a5568; text-decoration:underline;\">{WebUtility.HtmlEncode(unsubscribeText)}</a></p>");
                }
                sb.AppendLine("          </td></tr>");
                break;
            }
        }
    }

    public static string CompileBlocksToPlainText(List<EmailBlockDto> blocks)
    {
        var sb = new StringBuilder();

        foreach (var block in blocks)
        {
            var type = block.Type?.ToLowerInvariant() ?? "text";
            var props = block.Properties;

            switch (type)
            {
                case "heading":
                    var heading = GetStringProp(props, "text", string.Empty);
                    if (!string.IsNullOrWhiteSpace(heading))
                    {
                        sb.AppendLine(heading.ToUpperInvariant());
                        sb.AppendLine(new string('-', Math.Min(heading.Length, 40)));
                        sb.AppendLine();
                    }
                    break;

                case "text":
                case "paragraph":
                    var text = GetStringProp(props, "content", string.Empty);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text.Trim());
                        sb.AppendLine();
                    }
                    break;

                case "button":
                case "cta":
                    var label = GetStringProp(props, "label", "Link");
                    var url = GetStringProp(props, "url", "#");
                    sb.AppendLine($"[ {label}: {url} ]");
                    sb.AppendLine();
                    break;

                case "columns":
                case "two_column":
                    var left = GetStringProp(props, "leftContent", string.Empty);
                    var right = GetStringProp(props, "rightContent", string.Empty);
                    if (!string.IsNullOrWhiteSpace(left)) sb.AppendLine(left.Trim());
                    if (!string.IsNullOrWhiteSpace(right)) sb.AppendLine(right.Trim());
                    sb.AppendLine();
                    break;

                case "divider":
                    sb.AppendLine("----------------------------------------");
                    sb.AppendLine();
                    break;

                case "footer":
                    var company = GetStringProp(props, "companyName", "MindLift CRM");
                    var address = GetStringProp(props, "address", string.Empty);
                    sb.AppendLine(company);
                    if (!string.IsNullOrWhiteSpace(address)) sb.AppendLine(address);
                    sb.AppendLine("Unsubscribe: {{unsubscribeUrl}}");
                    sb.AppendLine();
                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ================= Merge Tag Resolution & Helpers =================

    public static string[] ExtractMergeTags(params string?[] sources)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var matches = MergeTagRegex.Matches(source);
            foreach (Match m in matches)
            {
                if (m.Groups.Count > 1)
                {
                    tags.Add(m.Groups[1].Value);
                }
            }
        }

        return tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static (string MergedText, List<string> MissingVariables) ReplaceTokens(
        string template,
        IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(template))
            return (string.Empty, new List<string>());

        var missing = new List<string>();

        var result = MergeTagRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (variables.TryGetValue(key, out var val))
            {
                return val;
            }

            // Also test stripped prefix (e.g. if key is "lead.firstName" test "firstName")
            var dotIndex = key.IndexOf('.');
            if (dotIndex > 0 && variables.TryGetValue(key[(dotIndex + 1)..], out var fallbackVal))
            {
                return fallbackVal;
            }

            missing.Add(key);
            return match.Value; // Leave unresolved token as-is
        });

        return (result, missing);
    }

    private static string PreserveMergeTags(string text)
    {
        // HtmlEncode text but keep {{tag}} placeholders pristine
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var parts = MergeTagRegex.Split(text);
        var matches = MergeTagRegex.Matches(text);

        var sb = new StringBuilder();
        int matchIdx = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            sb.Append(WebUtility.HtmlEncode(parts[i]));
            if (matchIdx < matches.Count)
            {
                sb.Append(matches[matchIdx].Value);
                matchIdx++;
            }
        }

        return sb.ToString();
    }

    private static string StripHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var noTags = Regex.Replace(html, "<.*?>", " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static List<EmailBlockDto> TryParseBlocks(string layoutJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<EmailBlockDto>>(layoutJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string GetStringProp(Dictionary<string, object?> props, string key, string fallback)
    {
        if (props.TryGetValue(key, out var val) && val != null)
        {
            if (val is JsonElement je && je.ValueKind == JsonValueKind.String)
                return je.GetString() ?? fallback;
            return val.ToString() ?? fallback;
        }
        return fallback;
    }

    private static int GetIntProp(Dictionary<string, object?> props, string key, int fallback)
    {
        if (props.TryGetValue(key, out var val) && val != null)
        {
            if (val is JsonElement je && je.TryGetInt32(out var parsedInt))
                return parsedInt;
            if (int.TryParse(val.ToString(), out var i))
                return i;
        }
        return fallback;
    }

    private static bool GetBoolProp(Dictionary<string, object?> props, string key, bool fallback)
    {
        if (props.TryGetValue(key, out var val) && val != null)
        {
            if (val is JsonElement je && (je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False))
                return je.GetBoolean();
            if (bool.TryParse(val.ToString(), out var b))
                return b;
        }
        return fallback;
    }

    private static EmailTemplateDto MapToDto(EmailTemplate entity, List<EmailBlockDto>? blocks = null)
    {
        blocks ??= TryParseBlocks(entity.LayoutJson);

        return new EmailTemplateDto
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Name = entity.Name,
            Subject = entity.Subject,
            PreheaderText = entity.PreheaderText,
            Category = entity.Category,
            Description = entity.Description,
            LayoutJson = entity.LayoutJson,
            Blocks = blocks,
            RenderedHtml = entity.RenderedHtml,
            PlainTextBody = entity.PlainTextBody,
            MergeTags = entity.MergeTags,
            ThumbnailUrl = entity.ThumbnailUrl,
            IsActive = entity.IsActive,
            IsPreset = entity.IsPreset,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    // ================= Built-in Starter Presets =================

    private static readonly List<PresetDefinition> BuiltInPresets = new()
    {
        new(
            "welcome-series",
            "Welcome New Customer",
            "Onboarding",
            "High-converting welcome template with getting-started button and key tips.",
            "Welcome to MindLift CRM, {{firstName}}!",
            "Here is everything you need to kickstart your journey.",
            new List<EmailBlockDto>
            {
                new() { Type = "header", Properties = new() { ["logoUrl"] = "https://mindliftcrm.com/logo.png", ["align"] = "center" } },
                new() { Type = "heading", Properties = new() { ["text"] = "Welcome aboard, {{firstName}}! 🚀", ["level"] = 1, ["align"] = "center" } },
                new() { Type = "text", Properties = new() { ["content"] = "We are thrilled to partner with {{company}}. MindLift CRM provides you with intelligent lead tracking, automated cadences, and crystal-clear deal pipelines to scale your revenue.", ["align"] = "center" } },
                new() { Type = "button", Properties = new() { ["label"] = "Go to Your Dashboard", ["url"] = "https://app.mindliftcrm.com/dashboard", ["align"] = "center", ["backgroundColor"] = "#3182ce" } },
                new() { Type = "divider", Properties = new() { ["color"] = "#e2e8f0" } },
                new() { Type = "columns", Properties = new() { ["leftContent"] = "<strong>⚡ Fast Setup</strong><br/>Import your contacts and connect your calendar in under 2 minutes.", ["rightContent"] = "<strong>🎯 Smart Scoring</strong><br/>Identify hot prospects instantly using behavioral signals." } },
                new() { Type = "footer", Properties = new() { ["companyName"] = "MindLift CRM Inc.", ["address"] = "500 Howard Street, San Francisco, CA" } }
            }
        ),
        new(
            "demo-followup",
            "Post-Demo Follow-Up",
            "Sales",
            "Clean salesperson follow-up template summarizing next steps and action items.",
            "Great speaking today, {{firstName}} - next steps for {{company}}",
            "Thank you for your time today. Here is a recap of what we covered.",
            new List<EmailBlockDto>
            {
                new() { Type = "heading", Properties = new() { ["text"] = "Great meeting you, {{firstName}}", ["level"] = 2 } },
                new() { Type = "text", Properties = new() { ["content"] = "Thank you for sharing your goals for {{company}}. As discussed, MindLift CRM will help your sales team streamline lead assignment and eliminate pipeline slippage." } },
                new() { Type = "text", Properties = new() { ["content"] = "Key next steps:\n• Review proposal for {{dealName}}\n• Schedule technical deep dive\n• Trial activation" } },
                new() { Type = "button", Properties = new() { ["label"] = "Review Proposal & Next Steps", ["url"] = "https://app.mindliftcrm.com/proposals/{{dealId}}", ["align"] = "left", ["backgroundColor"] = "#2b6cb0" } },
                new() { Type = "text", Properties = new() { ["content"] = "Let me know if you have any questions before then.\n\nBest regards,\nYour Sales Team" } },
                new() { Type = "footer", Properties = new() { ["companyName"] = "MindLift CRM Inc." } }
            }
        ),
        new(
            "re-engagement",
            "Quarterly Re-engagement Check-In",
            "Nurturing",
            "Warm personal check-in note to re-ignite cold conversations.",
            "Quick question regarding {{company}}",
            "Checking in on your sales goals this quarter.",
            new List<EmailBlockDto>
            {
                new() { Type = "text", Properties = new() { ["content"] = "Hi {{firstName}},\n\nI was reviewing our previous conversation about {{company}}'s sales initiatives and wanted to see how things are progressing this quarter." } },
                new() { Type = "text", Properties = new() { ["content"] = "We recently launched automated territory routing and lead scoring, which our customers are using to boost response speed by 40%." } },
                new() { Type = "button", Properties = new() { ["label"] = "Book 10-Minute Catch-Up", ["url"] = "https://calendly.com/mindlift/chat", ["align"] = "left", ["backgroundColor"] = "#38a169" } },
                new() { Type = "text", Properties = new() { ["content"] = "Would next Tuesday work for a quick 10-minute catch-up?\n\nCheers,\nAccount Executive" } },
                new() { Type = "footer", Properties = new() { ["companyName"] = "MindLift CRM Inc.", ["showUnsubscribe"] = true } }
            }
        ),
        new(
            "product-announcement",
            "Major Feature Announcement",
            "Marketing",
            "Eye-catching announcement template with hero banner and CTA.",
            "Introducing our latest update for {{company}}",
            "New features designed to accelerate your sales cycle.",
            new List<EmailBlockDto>
            {
                new() { Type = "header", Properties = new() { ["logoUrl"] = "https://mindliftcrm.com/logo.png", ["align"] = "center" } },
                new() { Type = "image", Properties = new() { ["src"] = "https://mindliftcrm.com/images/feature-banner.png", ["alt"] = "New Feature Banner" } },
                new() { Type = "heading", Properties = new() { ["text"] = "Supercharge Your Sales Pipeline", ["level"] = 1, ["align"] = "center" } },
                new() { Type = "text", Properties = new() { ["content"] = "We are excited to unveil our brand new visual drag-and-drop email builder, making it easier than ever to send high-converting messages to your leads.", ["align"] = "center" } },
                new() { Type = "button", Properties = new() { ["label"] = "Explore the New Features", ["url"] = "https://app.mindliftcrm.com/whats-new", ["align"] = "center", ["backgroundColor"] = "#805ad5" } },
                new() { Type = "footer", Properties = new() { ["companyName"] = "MindLift CRM Inc.", ["address"] = "500 Howard Street, San Francisco, CA" } }
            }
        )
    };

    private sealed record PresetDefinition(
        string Key,
        string Name,
        string Category,
        string Description,
        string Subject,
        string PreheaderText,
        List<EmailBlockDto> Blocks);
}

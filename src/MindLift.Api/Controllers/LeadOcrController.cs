using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using MindLift.Api.Auth;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}

[ApiController]
[Route("api/v1/leads")]
[Authorize]
[HasPermission(AppModule.Leads, PermissionAction.Create)]
[EnableRateLimiting("uploads")]
public sealed class LeadOcrController(LeadCardOcrService ocrService) : ControllerBase
{
    [HttpPost("parse-card")]
    public async Task<ActionResult<ExtractedLeadCardDto>> ParseCard(
        [FromBody] ParseCardOcrRequest request,
        CancellationToken ct = default)
    {
        var result = await ocrService.ExtractAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// True zero-buffer end-to-end streaming OCR endpoint.
    /// Reads incoming multipart/form-data or raw image bytes directly from Request.Body without ASP.NET IFormFile disk/memory buffering.
    /// </summary>
    [HttpPost("scan-card-file")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<ExtractedLeadCardDto>> ScanCardFile(CancellationToken ct = default)
    {
        var contentType = Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
        {
            throw new ValidationException("invalid_content_type", "Missing Content-Type header.");
        }

        // 1. Direct binary stream upload (e.g. image/jpeg, image/png, application/octet-stream)
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Request.Headers.TryGetValue("X-File-Name", out var headerName)
                ? headerName.ToString()
                : "card_scan.jpg";
            var directResult = await ocrService.ExtractFromDirectStreamAsync(Request.Body, fileName, ct);
            return Ok(directResult);
        }

        // 2. Genuine zero-buffer multipart stream parsing via MultipartReader
        if (contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            var mediaType = MediaTypeHeaderValue.Parse(contentType);
            var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new ValidationException("invalid_multipart", "Missing multipart boundary.");
            }

            var reader = new MultipartReader(boundary, Request.Body)
            {
                BodyLengthLimit = 16 * 1024 * 1024
            };

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) != null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition)
                    && contentDisposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
                    && (!string.IsNullOrEmpty(contentDisposition.FileName.Value) || !string.IsNullOrEmpty(contentDisposition.FileNameStar.Value)))
                {
                    var fileName = (contentDisposition.FileName.Value ?? contentDisposition.FileNameStar.Value ?? "card_scan.jpg").Trim('"');
                    var result = await ocrService.ExtractFromDirectStreamAsync(section.Body, fileName, ct);
                    return Ok(result);
                }
            }

            throw new ValidationException("empty_file", "No image file section found in multipart upload.");
        }

        throw new ValidationException("invalid_content_type", $"Unsupported Content-Type '{contentType}'. Expected multipart/form-data or image/jpeg.");
    }
}

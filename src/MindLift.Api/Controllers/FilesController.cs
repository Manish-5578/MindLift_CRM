using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Files;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// File upload and download. Any signed-in user may upload an unattached file; attaching to a record needs Edit on that
/// record's module, and reading an attached file needs View on it (checked in <see cref="FileService"/>).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/files")]
public sealed class FilesController(FileService files) : ControllerBase
{
    /// <summary>Multipart form: file, and optionally entityType plus entityId to attach it to a record.</summary>
    [HttpPost]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<ActionResult<StoredFileDto>> Upload(IFormFile? file, [FromForm] string? entityType, [FromForm] Guid? entityId, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["Choose a file to upload."] });
        }

        await using var stream = file.OpenReadStream();
        var dto = await files.UploadAsync(stream, file.FileName, entityType, entityId, cancellationToken);
        return Ok(dto);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StoredFileDto>>> List([FromQuery] string entityType, [FromQuery] Guid entityId, CancellationToken cancellationToken)
        => Ok(await files.ListForRecordAsync(entityType, entityId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var (file, content) = await files.OpenAsync(id, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await files.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

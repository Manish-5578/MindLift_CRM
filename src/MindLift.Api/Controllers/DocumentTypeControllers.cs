using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Documents.Extraction;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>Document types a workspace defines in Settings (Admin by default): what to recognise and which values to take out.</summary>
[ApiController]
[Route("api/v1/settings/document-types")]
public sealed class DocumentTypesController(DocumentTypeService types) : ControllerBase
{
    /// <summary>The largest sample file the test accepts, in bytes.</summary>
    private const long MaxSampleBytes = 25L * 1024 * 1024;

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentTypeDto>>> List(CancellationToken cancellationToken)
        => Ok(await types.ListAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentTypeDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await types.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<DocumentTypeDto>> Create([FromBody] SaveDocumentTypeRequest request, CancellationToken cancellationToken)
        => Ok(await types.SaveAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentTypeDto>> Update(Guid id, [FromBody] SaveDocumentTypeRequest request, CancellationToken cancellationToken)
        => Ok(await types.SaveAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await types.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Runs the type on a sample file and reports the fields it reads. Nothing is saved.</summary>
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("{id:guid}/test")]
    [RequestSizeLimit(25L * 1024 * 1024)]
    public async Task<ActionResult<DocumentTypeTestResultDto>> Test(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new MindLift.Application.Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["file"] = ["Choose a sample file."] });
        }

        if (file.Length > MaxSampleBytes)
        {
            throw new MindLift.Application.Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["file"] = ["The sample is larger than 25 MB."] });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var path = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var target = System.IO.File.Create(path))
            {
                await file.CopyToAsync(target, cancellationToken);
            }

            return Ok(await types.TestAsync(id, path, cancellationToken));
        }
        finally
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch (IOException)
            {
                // Temporary file.
            }
        }
    }
}

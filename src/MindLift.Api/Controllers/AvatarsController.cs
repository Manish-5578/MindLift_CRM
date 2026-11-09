using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;

namespace MindLift.Api.Controllers;

/// <summary>
/// Profile photos. A photo is kept under the caller's own company prefix, so nobody can read another company's photos,
/// and a person can only change their own.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/avatars")]
public sealed class AvatarsController(IFileStorage storage, ITenantContext tenant) : ControllerBase
{
    private const long MaxBytes = 3L * 1024 * 1024;

    private string KeyFor(Guid userId)
        => $"{tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.")}/avatars/{userId:N}";

    private static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "image/webp";
        return null;
    }

    [HttpPut("me")]
    [RequestSizeLimit(4L * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Choose a photo." });
        if (file.Length > MaxBytes) return BadRequest(new { error = "Photos can be up to 3 MB." });

        await using var stream = file.OpenReadStream();
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        if (Sniff(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) is null)
        {
            return BadRequest(new { error = "Use a JPG, PNG or WEBP photo." });
        }

        buffer.Position = 0;
        await storage.SaveAsync(KeyFor(tenant.UserId ?? throw new ForbiddenException()), buffer, ct);
        return NoContent();
    }

    [HttpDelete("me")]
    public async Task<IActionResult> Remove(CancellationToken ct)
    {
        await storage.DeleteAsync(KeyFor(tenant.UserId ?? throw new ForbiddenException()), ct);
        return NoContent();
    }

    /// <summary>A colleague's photo, or 404 when they have none (the app then shows their initials).</summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
    {
        var key = KeyFor(userId);
        if (!await storage.ExistsAsync(key, ct)) return NotFound();

        await using var read = await storage.OpenReadAsync(key, ct);
        var buffer = new MemoryStream();
        await read.CopyToAsync(buffer, ct);
        var type = Sniff(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) ?? "application/octet-stream";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=300";
        return File(buffer.ToArray(), type);
    }
}

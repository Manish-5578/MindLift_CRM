using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #33: Call recording and transcription (call-recording).
/// REST endpoints for recording consent settings, retrieving call audio recordings,
/// triggering speech-to-text transcriptions, sentiment analysis, action items,
/// receiving provider transcription webhooks, and GDPR recording erasure.
/// </summary>
[ApiController]
[Route("api/v1/telephony/recording")]
public sealed class CallRecordingController(ICallRecordingService recordingService) : ControllerBase
{
    // ==========================================
    // Public Provider Transcription Webhook
    // ==========================================

    [AllowAnonymous]
    [HttpPost("webhook/transcription")]
    public async Task<IActionResult> TranscriptionWebhook(
        [FromBody] TranscriptionWebhookRequest request,
        CancellationToken ct)
    {
        await recordingService.ProcessTranscriptionWebhookAsync(request, ct);
        return Ok(new { success = true });
    }

    // ==========================================
    // Authenticated Endpoints (RequiresFeature "call-recording")
    // ==========================================

    [Authorize]
    [RequiresFeature("call-recording")]
    [HttpGet("consent")]
    public async Task<ActionResult<RecordingConsentDto>> GetConsentSettings(CancellationToken ct)
    {
        var settings = await recordingService.GetConsentSettingsAsync(ct);
        return Ok(settings);
    }

    [Authorize]
    [RequiresFeature("call-recording")]
    [HttpPut("consent")]
    public async Task<ActionResult<RecordingConsentDto>> UpdateConsentSettings(
        [FromBody] UpdateRecordingConsentRequest request,
        CancellationToken ct)
    {
        var updated = await recordingService.UpdateConsentSettingsAsync(request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("call-recording")]
    [HttpGet("calls/{callId:guid}")]
    public async Task<ActionResult<CallRecordingDto>> GetRecording(
        Guid callId,
        CancellationToken ct)
    {
        var recording = await recordingService.GetRecordingAsync(callId, ct);
        return Ok(recording);
    }

    [Authorize]
    [RequiresFeature("call-recording")]
    [HttpPost("calls/{callId:guid}/transcribe")]
    public async Task<ActionResult<TranscriptionResultDto>> TranscribeCall(
        Guid callId,
        [FromBody] TranscribeCallRequest? request,
        CancellationToken ct)
    {
        var result = await recordingService.TranscribeCallAsync(callId, request, ct);
        return Ok(result);
    }

    [Authorize]
    [RequiresFeature("call-recording")]
    [HttpGet("calls/{callId:guid}/transcription")]
    public async Task<ActionResult<TranscriptionResultDto>> GetTranscription(
        Guid callId,
        CancellationToken ct)
    {
        var transcription = await recordingService.GetTranscriptionAsync(callId, ct);
        return Ok(transcription);
    }

    [Authorize]
    [RequiresFeature("call-recording")]
    [HttpDelete("calls/{callId:guid}")]
    public async Task<ActionResult<GdprDeleteRecordingResult>> DeleteRecording(
        Guid callId,
        CancellationToken ct)
    {
        var result = await recordingService.DeleteRecordingAsync(callId, ct);
        return Ok(result);
    }
}

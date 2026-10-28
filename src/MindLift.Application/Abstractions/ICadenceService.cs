using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ICadenceService
{
    Task<CadenceDto> CreateCadenceAsync(SaveCadenceRequest req, CancellationToken ct = default);
    Task<CadenceDto> GetCadenceByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CadenceDto>> ListCadencesAsync(string? targetType = null, string? status = null, CancellationToken ct = default);
    Task<CadenceDto> UpdateCadenceAsync(Guid id, SaveCadenceRequest req, CancellationToken ct = default);
    Task<bool> DeleteCadenceAsync(Guid id, CancellationToken ct = default);
    Task<CadenceDto> SetCadenceStatusAsync(Guid id, string status, CancellationToken ct = default);

    Task<CadenceEnrollmentDto> EnrollTargetAsync(EnrollCadenceTargetRequest req, CancellationToken ct = default);
    Task<CadenceEnrollmentDto> GetEnrollmentByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CadenceEnrollmentDto>> ListEnrollmentsAsync(Guid? cadenceId = null, string? targetType = null, Guid? targetId = null, string? status = null, CancellationToken ct = default);
    Task<CadenceEnrollmentDto> PauseEnrollmentAsync(Guid enrollmentId, CancellationToken ct = default);
    Task<CadenceEnrollmentDto> ResumeEnrollmentAsync(Guid enrollmentId, CancellationToken ct = default);
    Task<CadenceEnrollmentDto> UnenrollAsync(Guid enrollmentId, string? reason = null, CancellationToken ct = default);
    Task<CadenceEnrollmentDto> MarkRepliedAsync(Guid enrollmentId, string? notes = null, CancellationToken ct = default);

    Task<CadenceEnrollmentDto> ExecuteStepAsync(Guid enrollmentId, ExecuteCadenceStepRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<CadenceEnrollmentDto>> GetDueEnrollmentsAsync(CancellationToken ct = default);
}

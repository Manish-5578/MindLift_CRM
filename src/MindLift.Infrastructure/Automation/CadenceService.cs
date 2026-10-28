using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #21: Sales Cadences (cadences).
/// Service for multi-channel sales cadences, outreach sequences, target enrollment,
/// step scheduling, and prospect reply handling.
/// </summary>
public sealed class CadenceService(
    AppDbContext db,
    ITenantContext tenant,
    ILogger<CadenceService> logger) : ICadenceService
{
    public async Task<CadenceDto> CreateCadenceAsync(SaveCadenceRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Cadence name is required.");

        var targetType = string.IsNullOrWhiteSpace(req.TargetType) ? "Lead" : req.TargetType.Trim();
        var status = string.IsNullOrWhiteSpace(req.Status) ? "Draft" : req.Status.Trim();

        var cadence = new Cadence
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            Name = name,
            Description = req.Description?.Trim(),
            TargetType = targetType,
            Status = status,
            OwnerId = req.OwnerId ?? tenant.UserId,
            CreatedAt = DateTime.UtcNow
        };

        if (req.Steps != null && req.Steps.Count > 0)
        {
            int stepNum = 1;
            foreach (var s in req.Steps)
            {
                cadence.Steps.Add(new CadenceStep
                {
                    TenantId = cadence.TenantId,
                    StepNumber = stepNum++,
                    StepType = string.IsNullOrWhiteSpace(s.StepType) ? "Email" : s.StepType.Trim(),
                    WaitDays = Math.Max(0, s.WaitDays),
                    Subject = s.Subject?.Trim(),
                    Content = s.Content?.Trim(),
                    ActionType = string.IsNullOrWhiteSpace(s.ActionType) ? "ManualEmail" : s.ActionType.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        db.Cadences.Add(cadence);
        await db.SaveChangesAsync(ct);

        return MapCadenceDto(cadence);
    }

    public async Task<CadenceDto> GetCadenceByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cadence = await db.Cadences
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cadence", id);

        return MapCadenceDto(cadence);
    }

    public async Task<IReadOnlyList<CadenceDto>> ListCadencesAsync(string? targetType = null, string? status = null, CancellationToken ct = default)
    {
        var query = db.Cadences
            .Include(c => c.Steps)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(c => c.TargetType.ToLower() == targetType.ToLower());

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status.ToLower() == status.ToLower());

        var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return list.Select(MapCadenceDto).ToList();
    }

    public async Task<CadenceDto> UpdateCadenceAsync(Guid id, SaveCadenceRequest req, CancellationToken ct = default)
    {
        var cadence = await db.Cadences
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cadence", id);

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Cadence name is required.");

        cadence.Name = name;
        cadence.Description = req.Description?.Trim();
        cadence.TargetType = string.IsNullOrWhiteSpace(req.TargetType) ? cadence.TargetType : req.TargetType.Trim();
        cadence.Status = string.IsNullOrWhiteSpace(req.Status) ? cadence.Status : req.Status.Trim();
        if (req.OwnerId.HasValue) cadence.OwnerId = req.OwnerId.Value;
        cadence.UpdatedAt = DateTime.UtcNow;

        if (req.Steps != null)
        {
            cadence.Steps.Clear();
            int stepNum = 1;
            foreach (var s in req.Steps)
            {
                cadence.Steps.Add(new CadenceStep
                {
                    TenantId = cadence.TenantId,
                    StepNumber = stepNum++,
                    StepType = string.IsNullOrWhiteSpace(s.StepType) ? "Email" : s.StepType.Trim(),
                    WaitDays = Math.Max(0, s.WaitDays),
                    Subject = s.Subject?.Trim(),
                    Content = s.Content?.Trim(),
                    ActionType = string.IsNullOrWhiteSpace(s.ActionType) ? "ManualEmail" : s.ActionType.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return MapCadenceDto(cadence);
    }

    public async Task<bool> DeleteCadenceAsync(Guid id, CancellationToken ct = default)
    {
        var cadence = await db.Cadences.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cadence == null) return false;

        cadence.IsDeleted = true;
        cadence.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CadenceDto> SetCadenceStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var cadence = await db.Cadences
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Cadence", id);

        cadence.Status = status.Trim();
        cadence.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return MapCadenceDto(cadence);
    }

    public async Task<CadenceEnrollmentDto> EnrollTargetAsync(EnrollCadenceTargetRequest req, CancellationToken ct = default)
    {
        var cadence = await db.Cadences
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == req.CadenceId, ct)
            ?? throw new NotFoundException("Cadence", req.CadenceId);

        var targetType = req.TargetType.Trim();
        string? targetName = null;
        string? targetEmail = null;

        if (string.Equals(targetType, "Lead", StringComparison.OrdinalIgnoreCase))
        {
            var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.Id == req.TargetId && !l.IsDeleted, ct)
                ?? throw new NotFoundException("Lead", req.TargetId);
            targetName = $"{lead.FirstName} {lead.LastName}".Trim();
            targetEmail = lead.Email;
        }
        else if (string.Equals(targetType, "Contact", StringComparison.OrdinalIgnoreCase))
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == req.TargetId && !c.IsDeleted, ct)
                ?? throw new NotFoundException("Contact", req.TargetId);
            targetName = $"{contact.FirstName} {contact.LastName}".Trim();
            targetEmail = contact.NormalizedEmail;
        }
        else
        {
            throw new ValidationException("TargetType", "TargetType must be 'Lead' or 'Contact'.");
        }

        // Prevent duplicate active enrollments
        var existing = await db.CadenceEnrollments.FirstOrDefaultAsync(e =>
            e.CadenceId == req.CadenceId &&
            e.TargetId == req.TargetId &&
            (e.Status == "Active" || e.Status == "Paused") &&
            !e.IsDeleted, ct);

        if (existing != null)
            throw new ValidationException("TargetId", "Target is already actively enrolled in this cadence.");

        var orderedSteps = cadence.Steps.OrderBy(s => s.StepNumber).ToList();
        var firstStep = orderedSteps.FirstOrDefault();
        var nextDue = DateTime.UtcNow.AddDays(firstStep != null ? firstStep.WaitDays : 0);

        var enrollment = new CadenceEnrollment
        {
            TenantId = cadence.TenantId,
            CadenceId = cadence.Id,
            TargetType = targetType,
            TargetId = req.TargetId,
            CurrentStepNumber = 1,
            Status = "Active",
            EnrolledAt = DateTime.UtcNow,
            NextStepDueAt = nextDue,
            CreatedAt = DateTime.UtcNow
        };

        cadence.ActiveEnrollmentCount++;
        db.CadenceEnrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);

        return MapEnrollmentDto(enrollment, cadence, targetName, targetEmail, orderedSteps);
    }

    public async Task<CadenceEnrollmentDto> GetEnrollmentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var enrollment = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("CadenceEnrollment", id);

        var (name, email) = await ResolveTargetAsync(enrollment.TargetType, enrollment.TargetId, ct);
        return MapEnrollmentDto(enrollment, enrollment.Cadence, name, email, enrollment.Cadence.Steps.OrderBy(s => s.StepNumber).ToList());
    }

    public async Task<IReadOnlyList<CadenceEnrollmentDto>> ListEnrollmentsAsync(
        Guid? cadenceId = null,
        string? targetType = null,
        Guid? targetId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .AsNoTracking()
            .AsQueryable();

        if (cadenceId.HasValue) query = query.Where(e => e.CadenceId == cadenceId.Value);
        if (targetId.HasValue) query = query.Where(e => e.TargetId == targetId.Value);
        if (!string.IsNullOrWhiteSpace(targetType)) query = query.Where(e => e.TargetType.ToLower() == targetType.ToLower());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status.ToLower() == status.ToLower());

        var enrollments = await query.OrderByDescending(e => e.EnrolledAt).ToListAsync(ct);
        var results = new List<CadenceEnrollmentDto>();

        foreach (var e in enrollments)
        {
            var (name, email) = await ResolveTargetAsync(e.TargetType, e.TargetId, ct);
            results.Add(MapEnrollmentDto(e, e.Cadence, name, email, e.Cadence.Steps.OrderBy(s => s.StepNumber).ToList()));
        }

        return results;
    }

    public async Task<CadenceEnrollmentDto> PauseEnrollmentAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var enrollment = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct)
            ?? throw new NotFoundException("CadenceEnrollment", enrollmentId);

        enrollment.Status = "Paused";
        enrollment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (name, email) = await ResolveTargetAsync(enrollment.TargetType, enrollment.TargetId, ct);
        return MapEnrollmentDto(enrollment, enrollment.Cadence, name, email, enrollment.Cadence.Steps.OrderBy(s => s.StepNumber).ToList());
    }

    public async Task<CadenceEnrollmentDto> ResumeEnrollmentAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var enrollment = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct)
            ?? throw new NotFoundException("CadenceEnrollment", enrollmentId);

        enrollment.Status = "Active";
        if (enrollment.NextStepDueAt.HasValue && enrollment.NextStepDueAt.Value < DateTime.UtcNow)
        {
            enrollment.NextStepDueAt = DateTime.UtcNow;
        }
        enrollment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (name, email) = await ResolveTargetAsync(enrollment.TargetType, enrollment.TargetId, ct);
        return MapEnrollmentDto(enrollment, enrollment.Cadence, name, email, enrollment.Cadence.Steps.OrderBy(s => s.StepNumber).ToList());
    }

    public async Task<CadenceEnrollmentDto> UnenrollAsync(Guid enrollmentId, string? reason = null, CancellationToken ct = default)
    {
        var enrollment = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct)
            ?? throw new NotFoundException("CadenceEnrollment", enrollmentId);

        if (enrollment.Status == "Active" || enrollment.Status == "Paused")
        {
            enrollment.Cadence.ActiveEnrollmentCount = Math.Max(0, enrollment.Cadence.ActiveEnrollmentCount - 1);
        }

        enrollment.Status = "Unenrolled";
        enrollment.NextStepDueAt = null;
        enrollment.UpdatedAt = DateTime.UtcNow;

        var exec = new CadenceStepExecution
        {
            TenantId = enrollment.TenantId,
            EnrollmentId = enrollment.Id,
            StepNumber = enrollment.CurrentStepNumber,
            Result = "Skipped",
            Notes = string.IsNullOrWhiteSpace(reason) ? "Unenrolled from cadence." : $"Unenrolled: {reason.Trim()}",
            ExecutedByUserId = tenant.UserId,
            ExecutedAt = DateTime.UtcNow
        };
        db.CadenceStepExecutions.Add(exec);

        await db.SaveChangesAsync(ct);

        var (name, email) = await ResolveTargetAsync(enrollment.TargetType, enrollment.TargetId, ct);
        return MapEnrollmentDto(enrollment, enrollment.Cadence, name, email, enrollment.Cadence.Steps.OrderBy(s => s.StepNumber).ToList());
    }

    public async Task<CadenceEnrollmentDto> MarkRepliedAsync(Guid enrollmentId, string? notes = null, CancellationToken ct = default)
    {
        var enrollment = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct)
            ?? throw new NotFoundException("CadenceEnrollment", enrollmentId);

        if (enrollment.Status == "Active" || enrollment.Status == "Paused")
        {
            enrollment.Cadence.ActiveEnrollmentCount = Math.Max(0, enrollment.Cadence.ActiveEnrollmentCount - 1);
        }

        enrollment.Status = "Replied";
        enrollment.NextStepDueAt = null;
        enrollment.CompletedAt = DateTime.UtcNow;
        enrollment.UpdatedAt = DateTime.UtcNow;

        var exec = new CadenceStepExecution
        {
            TenantId = enrollment.TenantId,
            EnrollmentId = enrollment.Id,
            StepNumber = enrollment.CurrentStepNumber,
            Result = "Replied",
            Notes = notes ?? "Prospect replied to outreach.",
            ExecutedByUserId = tenant.UserId,
            ExecutedAt = DateTime.UtcNow
        };
        db.CadenceStepExecutions.Add(exec);

        await db.SaveChangesAsync(ct);

        var (name, email) = await ResolveTargetAsync(enrollment.TargetType, enrollment.TargetId, ct);
        return MapEnrollmentDto(enrollment, enrollment.Cadence, name, email, enrollment.Cadence.Steps.OrderBy(s => s.StepNumber).ToList());
    }

    public async Task<CadenceEnrollmentDto> ExecuteStepAsync(Guid enrollmentId, ExecuteCadenceStepRequest req, CancellationToken ct = default)
    {
        var enrollment = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct)
            ?? throw new NotFoundException("CadenceEnrollment", enrollmentId);

        var orderedSteps = enrollment.Cadence.Steps.OrderBy(s => s.StepNumber).ToList();
        var currentStep = orderedSteps.FirstOrDefault(s => s.StepNumber == enrollment.CurrentStepNumber);

        var exec = new CadenceStepExecution
        {
            TenantId = enrollment.TenantId,
            EnrollmentId = enrollment.Id,
            StepId = currentStep?.Id,
            StepNumber = enrollment.CurrentStepNumber,
            Result = string.IsNullOrWhiteSpace(req.Result) ? "Completed" : req.Result.Trim(),
            Notes = req.Notes?.Trim(),
            ExecutedByUserId = tenant.UserId,
            ExecutedAt = DateTime.UtcNow
        };
        db.CadenceStepExecutions.Add(exec);

        if (string.Equals(req.Result, "Replied", StringComparison.OrdinalIgnoreCase))
        {
            enrollment.Status = "Replied";
            enrollment.CompletedAt = DateTime.UtcNow;
            enrollment.NextStepDueAt = null;
            enrollment.Cadence.ActiveEnrollmentCount = Math.Max(0, enrollment.Cadence.ActiveEnrollmentCount - 1);
        }
        else
        {
            var nextStep = orderedSteps.FirstOrDefault(s => s.StepNumber > enrollment.CurrentStepNumber);
            if (nextStep != null)
            {
                enrollment.CurrentStepNumber = nextStep.StepNumber;
                enrollment.NextStepDueAt = DateTime.UtcNow.AddDays(nextStep.WaitDays);
            }
            else
            {
                // Sequence finished!
                enrollment.Status = "Completed";
                enrollment.CompletedAt = DateTime.UtcNow;
                enrollment.NextStepDueAt = null;
                enrollment.Cadence.ActiveEnrollmentCount = Math.Max(0, enrollment.Cadence.ActiveEnrollmentCount - 1);
                enrollment.Cadence.CompletedEnrollmentCount++;
            }
        }

        enrollment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (name, email) = await ResolveTargetAsync(enrollment.TargetType, enrollment.TargetId, ct);
        return MapEnrollmentDto(enrollment, enrollment.Cadence, name, email, orderedSteps);
    }

    public async Task<IReadOnlyList<CadenceEnrollmentDto>> GetDueEnrollmentsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.CadenceEnrollments
            .Include(e => e.Cadence)
                .ThenInclude(c => c.Steps)
            .Where(e => e.Status == "Active" && e.NextStepDueAt.HasValue && e.NextStepDueAt.Value <= now)
            .OrderBy(e => e.NextStepDueAt)
            .ToListAsync(ct);

        var list = new List<CadenceEnrollmentDto>();
        foreach (var e in due)
        {
            var (name, email) = await ResolveTargetAsync(e.TargetType, e.TargetId, ct);
            list.Add(MapEnrollmentDto(e, e.Cadence, name, email, e.Cadence.Steps.OrderBy(s => s.StepNumber).ToList()));
        }
        return list;
    }

    private async Task<(string? Name, string? Email)> ResolveTargetAsync(string targetType, Guid targetId, CancellationToken ct)
    {
        if (string.Equals(targetType, "Lead", StringComparison.OrdinalIgnoreCase))
        {
            var lead = await db.Set<Lead>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == targetId, ct);
            if (lead != null) return ($"{lead.FirstName} {lead.LastName}".Trim(), lead.Email);
        }
        else if (string.Equals(targetType, "Contact", StringComparison.OrdinalIgnoreCase))
        {
            var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == targetId, ct);
            if (contact != null) return ($"{contact.FirstName} {contact.LastName}".Trim(), contact.NormalizedEmail);
        }
        return (null, null);
    }

    private static CadenceDto MapCadenceDto(Cadence c)
    {
        return new CadenceDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            TargetType = c.TargetType,
            Status = c.Status,
            OwnerId = c.OwnerId,
            ActiveEnrollmentCount = c.ActiveEnrollmentCount,
            CompletedEnrollmentCount = c.CompletedEnrollmentCount,
            Steps = c.Steps.OrderBy(s => s.StepNumber).Select(s => new CadenceStepDto
            {
                Id = s.Id,
                CadenceId = s.CadenceId,
                StepNumber = s.StepNumber,
                StepType = s.StepType,
                WaitDays = s.WaitDays,
                Subject = s.Subject,
                Content = s.Content,
                ActionType = s.ActionType
            }).ToList(),
            CreatedAt = c.CreatedAt
        };
    }

    private static CadenceEnrollmentDto MapEnrollmentDto(
        CadenceEnrollment e,
        Cadence c,
        string? targetName,
        string? targetEmail,
        List<CadenceStep> orderedSteps)
    {
        var currentStep = orderedSteps.FirstOrDefault(s => s.StepNumber == e.CurrentStepNumber);

        return new CadenceEnrollmentDto
        {
            Id = e.Id,
            CadenceId = e.CadenceId,
            CadenceName = c.Name,
            TargetType = e.TargetType,
            TargetId = e.TargetId,
            TargetName = targetName,
            TargetEmail = targetEmail,
            CurrentStepNumber = e.CurrentStepNumber,
            TotalSteps = orderedSteps.Count,
            CurrentStepType = currentStep?.StepType,
            CurrentStepSubject = currentStep?.Subject,
            Status = e.Status,
            EnrolledAt = e.EnrolledAt,
            NextStepDueAt = e.NextStepDueAt,
            CompletedAt = e.CompletedAt
        };
    }
}

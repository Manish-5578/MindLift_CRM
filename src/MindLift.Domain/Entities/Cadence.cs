using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #21: Sales Cadences (cadences).
/// A sequential automated multi-channel outreach campaign (Email, Call, Task, LinkedIn, Wait)
/// for prospecting leads and contacts.
/// </summary>
[Audited]
public sealed class Cadence : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetType { get; set; } = "Lead"; // Lead, Contact, Both
    public string Status { get; set; } = "Draft"; // Draft, Active, Paused, Archived
    public Guid? OwnerId { get; set; }
    public int ActiveEnrollmentCount { get; set; }
    public int CompletedEnrollmentCount { get; set; }

    public ICollection<CadenceStep> Steps { get; set; } = new List<CadenceStep>();
    public ICollection<CadenceEnrollment> Enrollments { get; set; } = new List<CadenceEnrollment>();
}

[Audited]
public sealed class CadenceStep : TenantEntity
{
    public Guid CadenceId { get; set; }
    public Cadence Cadence { get; set; } = null!;
    public int StepNumber { get; set; }
    public string StepType { get; set; } = "Email"; // Email, Call, Task, LinkedIn, Wait
    public int WaitDays { get; set; } = 1; // Delay after previous step
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public string ActionType { get; set; } = "ManualEmail"; // ManualEmail, AutomatedEmail, CallPrompt, GeneralTask
}

[Audited]
public sealed class CadenceEnrollment : TenantEntity
{
    public Guid CadenceId { get; set; }
    public Cadence Cadence { get; set; } = null!;
    public string TargetType { get; set; } = "Lead"; // Lead, Contact
    public Guid TargetId { get; set; }
    public int CurrentStepNumber { get; set; } = 1;
    public string Status { get; set; } = "Active"; // Active, Paused, Completed, Replied, Unenrolled
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime? NextStepDueAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<CadenceStepExecution> StepExecutions { get; set; } = new List<CadenceStepExecution>();
}

[Audited]
public sealed class CadenceStepExecution : TenantEntity
{
    public Guid EnrollmentId { get; set; }
    public CadenceEnrollment Enrollment { get; set; } = null!;
    public Guid? StepId { get; set; }
    public int StepNumber { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public string Result { get; set; } = "Completed"; // Completed, Skipped, Replied, Failed
    public string? Notes { get; set; }
    public Guid? ExecutedByUserId { get; set; }
}

namespace MindLift.Shared.Contracts;

public sealed class SaveCadenceRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetType { get; set; } = "Lead"; // Lead, Contact, Both
    public string Status { get; set; } = "Draft"; // Draft, Active, Paused, Archived
    public Guid? OwnerId { get; set; }
    public List<SaveCadenceStepRequest> Steps { get; set; } = [];
}

public sealed class SaveCadenceStepRequest
{
    public Guid? Id { get; set; }
    public int StepNumber { get; set; }
    public string StepType { get; set; } = "Email"; // Email, Call, Task, LinkedIn, Wait
    public int WaitDays { get; set; } = 1;
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public string ActionType { get; set; } = "ManualEmail";
}

public sealed class CadenceStepDto
{
    public Guid Id { get; set; }
    public Guid CadenceId { get; set; }
    public int StepNumber { get; set; }
    public string StepType { get; set; } = string.Empty;
    public int WaitDays { get; set; }
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public string ActionType { get; set; } = string.Empty;
}

public sealed class CadenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetType { get; set; } = "Lead";
    public string Status { get; set; } = "Draft";
    public Guid? OwnerId { get; set; }
    public int ActiveEnrollmentCount { get; set; }
    public int CompletedEnrollmentCount { get; set; }
    public List<CadenceStepDto> Steps { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public sealed class EnrollCadenceTargetRequest
{
    public Guid CadenceId { get; set; }
    public string TargetType { get; set; } = "Lead"; // Lead, Contact
    public Guid TargetId { get; set; }
}

public sealed class CadenceEnrollmentDto
{
    public Guid Id { get; set; }
    public Guid CadenceId { get; set; }
    public string CadenceName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string? TargetName { get; set; }
    public string? TargetEmail { get; set; }
    public int CurrentStepNumber { get; set; }
    public int TotalSteps { get; set; }
    public string? CurrentStepType { get; set; }
    public string? CurrentStepSubject { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime EnrolledAt { get; set; }
    public DateTime? NextStepDueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class ExecuteCadenceStepRequest
{
    public string Result { get; set; } = "Completed"; // Completed, Skipped, Replied, Failed
    public string? Notes { get; set; }
}

public sealed class CadenceStepExecutionDto
{
    public Guid Id { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid? StepId { get; set; }
    public int StepNumber { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? ExecutedByUserId { get; set; }
}

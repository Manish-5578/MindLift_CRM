namespace MindLift.Shared.Contracts;

public sealed class SaveSlaPolicyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;
    public int MaxResponseMinutes { get; set; } = 60;
    public int WarningThresholdPercentage { get; set; } = 80;
    public string[] Sources { get; set; } = [];
    public string[] Ratings { get; set; } = [];
    public Guid? EscalationUserId { get; set; }
    public string EscalationAction { get; set; } = "ReassignAndNotify"; // NotifyOnly, Reassign, ReassignAndNotify
}

public sealed class SlaPolicyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
    public int MaxResponseMinutes { get; set; }
    public int WarningThresholdPercentage { get; set; }
    public string[] Sources { get; set; } = [];
    public string[] Ratings { get; set; } = [];
    public Guid? EscalationUserId { get; set; }
    public string? EscalationUserName { get; set; }
    public string EscalationAction { get; set; } = "ReassignAndNotify";
    public DateTime CreatedAt { get; set; }
}

public sealed class RecordResponseRequest
{
    public DateTime? RespondedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class LeadSlaTrackerDto
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public Guid PolicyId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public DateTime DueAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? BreachedAt { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Met, Breached
    public bool Escalated { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public Guid? EscalatedToUserId { get; set; }
    public string? EscalatedToUserName { get; set; }
    public string? Notes { get; set; }
    public bool IsBreached { get; set; }
    public double MinutesRemaining { get; set; }
}

public sealed class SlaCheckResultDto
{
    public int CheckedCount { get; set; }
    public int MetCount { get; set; }
    public int WarningCount { get; set; }
    public int BreachedCount { get; set; }
    public int EscalatedCount { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

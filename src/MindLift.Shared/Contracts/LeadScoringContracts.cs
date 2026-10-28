namespace MindLift.Shared.Contracts;

public sealed class SaveLeadScoringRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string RuleType { get; set; } = "Demographic"; // Demographic, Behavioral, Decay, Custom
    public int Points { get; set; }
    public string ConditionField { get; set; } = string.Empty;
    public string Operator { get; set; } = "Equals"; // Equals, NotEquals, Contains, GreaterThan, LessThan, InList, Occurred
    public string ConditionValue { get; set; } = string.Empty;
    public int Order { get; set; } = 1;
}

public sealed class LeadScoringRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string RuleType { get; set; } = "Demographic";
    public int Points { get; set; }
    public string ConditionField { get; set; } = string.Empty;
    public string Operator { get; set; } = "Equals";
    public string ConditionValue { get; set; } = string.Empty;
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class LeadScoreLogDto
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public Guid? RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int Points { get; set; }
    public int RunningScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ScoredAt { get; set; }
}

public sealed class LeadScoreResultDto
{
    public Guid LeadId { get; set; }
    public int Score { get; set; }
    public string ScoreGrade { get; set; } = "Cold"; // Hot, Warm, Cold
    public DateTime? LastScoredAt { get; set; }
    public int AppliedRulesCount { get; set; }
    public List<LeadScoreLogDto> AppliedRules { get; set; } = [];
}

public sealed class BatchScoreRequest
{
    public List<Guid>? LeadIds { get; set; }
}

public sealed class BatchScoreResultDto
{
    public int TotalEvaluated { get; set; }
    public int TotalHotLeads { get; set; }
    public int TotalWarmLeads { get; set; }
    public int TotalColdLeads { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

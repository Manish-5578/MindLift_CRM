using System.Text.Json;
using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>Decides who gets a new lead (plan 7.3, step 1 of assignment).</summary>
[Audited]
public sealed class AssignmentRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }

    /// <summary>A FilterGroup (JSON) a new lead must match. Null matches every lead.</summary>
    public JsonDocument? Conditions { get; set; }

    /// <summary>RoundRobin, LoadBalanced or SpecificUser.</summary>
    public string Method { get; set; } = "RoundRobin";

    /// <summary>JSON array of user ids.</summary>
    public JsonDocument UserIds { get; set; } = JsonDocument.Parse("[]");

    public int LastAssignedIndex { get; set; } = -1;
}

/// <summary>A public form that creates leads from a website (plan 7.3, source 3).</summary>
[Audited]
public sealed class WebForm : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Random public token used in the submission URL. Unique across tenants.</summary>
    public string PublicToken { get; set; } = string.Empty;

    /// <summary>JSON array of { fieldKey, label, required } the form accepts.</summary>
    public JsonDocument Fields { get; set; } = JsonDocument.Parse("[]");

    public string DefaultSource { get; set; } = "website";
    public string SuccessMessage { get; set; } = "Thank you. We will be in touch soon.";
    public bool IsActive { get; set; } = true;
    public int SubmissionCount { get; set; }
}

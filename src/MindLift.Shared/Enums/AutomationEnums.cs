namespace MindLift.Shared.Enums;

/// <summary>
/// Triggers for automation rules (TCA engine).
/// </summary>
public enum AutomationTriggerType
{
    /// <summary>Fires when a new record is created.</summary>
    RecordCreated = 1,

    /// <summary>Fires when an existing record is updated (any field).</summary>
    RecordUpdated = 2,

    /// <summary>Fires when a specific field value changes.</summary>
    FieldChanged = 3,

    /// <summary>Fires when a deal changes its pipeline stage.</summary>
    StageChanged = 4,
}

/// <summary>
/// Actions executed by automation rules.
/// </summary>
public enum AutomationActionType
{
    /// <summary>Updates a system or custom field on the record.</summary>
    UpdateField = 1,

    /// <summary>Creates a linked task/activity.</summary>
    CreateTask = 2,

    /// <summary>Sends an in-app SignalR notification / toast.</summary>
    SendNotification = 3,

    /// <summary>Sends an email notification.</summary>
    SendEmail = 4,

    /// <summary>Calls an external HTTP webhook.</summary>
    Webhook = 5,
}

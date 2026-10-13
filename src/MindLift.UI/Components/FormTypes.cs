using MindLift.Shared.Contracts;

namespace MindLift.UI.Components;

/// <summary>How a field is edited and shown.</summary>
public enum FormFieldKind
{
    Text,
    Email,
    Phone,
    Url,
    Multiline,
    Picklist,
    User,
    Account,
    Contact,
    Number,
    Date,
    DateTime,
    Checkbox,
    Users,
}

/// <summary>One field of a record form. Columns is 6 for half width, 12 for full width.</summary>
public sealed record FormField(
    string Key,
    string Label,
    FormFieldKind Kind = FormFieldKind.Text,
    bool Required = false,
    IReadOnlyList<PicklistValueDto>? Options = null,
    string? TestId = null,
    int Columns = 12);

/// <summary>A record offered by a picker: its id and what to show.</summary>
public sealed record LookupItem(Guid Id, string Text);

/// <summary>One line of a record's history.</summary>
public sealed record TimelineItem(DateTime? WhenUtc, string Title, string? Detail = null, string? Icon = null, string? Href = null);

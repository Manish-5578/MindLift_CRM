using Microsoft.AspNetCore.Components;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.UI.Components;

/// <summary>One column of an <see cref="EntityListPage{TItem}"/>. The first column is the primary one: avatar, name and a second line.</summary>
public sealed class ListColumn<TItem>
{
    public string Header { get; init; } = string.Empty;

    /// <summary>What to show in the cell for a row.</summary>
    public RenderFragment<TItem> Cell { get; init; } = _ => _ => { };

    /// <summary>Hidden on tablets (600 to 959 px). The plan keeps name, status, owner and created there.</summary>
    public bool HideOnTablet { get; init; }

    /// <summary>Right-aligned, for money and numbers.</summary>
    public bool AlignEnd { get; init; }

    /// <summary>The field key to sort by when the header is clicked. Leave empty for a column that cannot be sorted.</summary>
    public string? SortField { get; init; }
}

/// <summary>Thrown by a list's loader when the server answered with an error. The message is shown to the user as it is.</summary>
public sealed class ListLoadException(string message) : Exception(message);

/// <summary>
/// A tab above the list: a named filter such as "My open leads". A view a user saved also carries its sort and visible columns,
/// and <c>SavedId</c> so it can be deleted.
/// </summary>
public sealed record ListView(string Key, string Label, FilterGroup? Filter = null)
{
    public Guid? SavedId { get; init; }
    public string? SortField { get; init; }
    public string? SortDir { get; init; }

    /// <summary>The headers of the columns to show. Empty shows the list's usual columns.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>True when the current user may delete it (their own view, not one a colleague shared).</summary>
    public bool CanDelete { get; init; }

    public const string SavedPrefix = "saved-";

    public static ListView FromSaved(SavedViewDto view, Guid? currentUserId) => new(SavedPrefix + view.Id.ToString("N"), view.Name, view.Filter)
    {
        SavedId = view.Id,
        SortField = view.SortField,
        SortDir = view.SortDir,
        Columns = view.Columns,
        CanDelete = view.OwnerUserId is null || view.OwnerUserId == currentUserId,
    };
}

/// <summary>
/// The records a bulk action applies to: the ticked rows (<c>Ids</c>), or every record the list matches (<c>AllMatching</c>, after "Select all").
/// Call <c>Done</c> after the action so the selection is cleared and the list reloaded.
/// </summary>
public sealed record BulkSelection(IReadOnlyList<Guid> Ids, ListQuery? AllMatching, int Count, Func<Task> Done);

/// <summary>What "+ Save view" asks the page to store: the current filter, sort and visible columns, under a name.</summary>
public sealed record SaveViewRequest(string Name, bool IsShared, FilterGroup? Filter, string? SortField, string? SortDir, IReadOnlyList<string> Columns);

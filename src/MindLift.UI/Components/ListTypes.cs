using Microsoft.AspNetCore.Components;
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

/// <summary>A tab above the list: a named filter such as "My open leads".</summary>
public sealed record ListView(string Key, string Label, FilterGroup? Filter = null);

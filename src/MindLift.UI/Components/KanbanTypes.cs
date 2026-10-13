namespace MindLift.UI.Components;

/// <summary>One column of a <see cref="KanbanBoard{TItem}"/>, for example a lead status or a deal stage.</summary>
/// <param name="Key">Stable code of the column. It is what a moved card is sent to.</param>
/// <param name="Label">Name shown in the column header.</param>
/// <param name="Color">Colour of the dot in the header, as a CSS colour. Optional.</param>
/// <param name="Total">How many records the column holds in all, which can be more than the cards loaded.</param>
/// <param name="Summary">A second line in the header, such as a money total. Optional.</param>
public sealed record KanbanColumn(string Key, string Label, string? Color = null, int Total = 0, string? Summary = null);

/// <summary>A card dropped on another column, or moved there with the card menu.</summary>
public sealed record KanbanMove<TItem>(TItem Item, string FromKey, string ToKey);

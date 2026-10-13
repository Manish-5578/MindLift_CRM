namespace MindLift.UI.Components;

/// <summary>Maps a meaning to a colour token (plan 18.2, status colour map).</summary>
public static class StatusColors
{
    public static string For(string category) => category switch
    {
        "New" or "Open" or "Draft" or "Uploaded" or "Queued" => "var(--ml-info)",
        "Working" or "Processing" or "InProgress" => "var(--ml-primary)",
        "Qualified" or "NeedsReview" or "Due" or "Expiring" => "var(--ml-warning)",
        "Won" or "Converted" or "Completed" or "Approved" or "Paid" => "var(--ml-success)",
        "Lost" or "Disqualified" or "Failed" or "Rejected" or "Overdue" or "Expired" => "var(--ml-error)",
        _ => "var(--ml-text-3)",
    };
}

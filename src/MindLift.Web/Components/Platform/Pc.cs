using System.Globalization;
using MudBlazor;

namespace MindLift.Web.Components.Platform;

/// <summary>Small helpers the platform console shares: colours, formatting, the icon of each module.</summary>
public static class Pc
{
    private static readonly string[][] Gradients =
    [
        ["#6366F1", "#8B5CF6"], ["#06B6D4", "#3B82F6"], ["#F59E0B", "#F43F5E"], ["#10B981", "#06B6D4"],
        ["#EC4899", "#8B5CF6"], ["#F97316", "#EAB308"], ["#14B8A6", "#6366F1"], ["#EF4444", "#F59E0B"]
    ];

    /// <summary>The same company always gets the same colours.</summary>
    public static string Gradient(string seed)
    {
        var h = 0;
        foreach (var c in seed) h = unchecked(h * 31 + c);
        var g = Gradients[Math.Abs(h) % Gradients.Length];
        return $"linear-gradient(135deg, {g[0]}, {g[1]})";
    }

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant() : $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
    }

    public static string StatusLabel(string status) => status switch
    {
        "Active" => "Active", "Trial" => "On trial", "PastDue" => "Past due", "Suspended" => "Suspended", "Cancelled" => "Cancelled", _ => status
    };

    public static string StatusClass(string status) => status switch
    {
        "Active" => "active", "Trial" => "trial", "PastDue" => "pastdue", "Suspended" => "suspended", _ => "cancelled"
    };

    public static string HealthColor(int h) => h >= 70 ? "#10B981" : h >= 40 ? "#F59E0B" : "#F43F5E";

    public static string Ago(DateTime? at)
    {
        if (at is null) return "never";
        var span = DateTime.UtcNow - at.Value.ToUniversalTime();
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return at.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public static string Money(decimal v) => v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    public static string Num(int v) => v >= 10_000 ? (v / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k" : v.ToString("N0", CultureInfo.InvariantCulture);
    public static string Date(DateTime? d) => d?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "-";

    /// <summary>The Material icon for the icon name the module catalogue gives.</summary>
    public static string Icon(string name) => name switch
    {
        "dashboard" => Icons.Material.Filled.SpaceDashboard,
        "leads" => Icons.Material.Filled.PersonSearch,
        "contacts" => Icons.Material.Filled.Contacts,
        "accounts" => Icons.Material.Filled.Business,
        "deals" => Icons.Material.Filled.Handshake,
        "activities" => Icons.Material.Filled.TaskAlt,
        "calendar" => Icons.Material.Filled.CalendarMonth,
        "communication" or "inbox" => Icons.Material.Filled.MarkChatUnread,
        "reports" => Icons.Material.Filled.Insights,
        "products" => Icons.Material.Filled.Inventory2,
        "quotations" or "quotes" => Icons.Material.Filled.RequestQuote,
        "invoices" => Icons.Material.Filled.ReceiptLong,
        "documents" => Icons.Material.Filled.DocumentScanner,
        "automation" => Icons.Material.Filled.AutoAwesome,
        "portal" => Icons.Material.Filled.Public,
        "notifications" or "bell" => Icons.Material.Filled.NotificationsActive,
        "users" or "people" => Icons.Material.Filled.Groups,
        "settings" => Icons.Material.Filled.Settings,
        "tickets" => Icons.Material.Filled.SupportAgent,
        "projects" => Icons.Material.Filled.AccountTree,
        "campaigns" => Icons.Material.Filled.Campaign,
        "integrations" => Icons.Material.Filled.Hub,
        _ => Icons.Material.Filled.Extension
    };
}

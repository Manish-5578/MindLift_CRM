namespace MindLift.Shared.Contracts;

/// <summary>One switchable capability and whether it is on for the current company.</summary>
public sealed record FeatureFlagDto(string Key, string Name, string Phase, bool Enabled);

/// <summary>Turns a capability on or off.</summary>
public sealed record SetFeatureRequest(bool Enabled);

/// <summary>Every capability that can be switched, so the server rejects any key it does not know.</summary>
public static class FeatureCatalog
{
    public sealed record Entry(string Key, string Name, string Phase);

    public static readonly IReadOnlyList<Entry> All =
    [
        // Phase 1: core sales data model
        new("lead-conversion", "Lead conversion overhaul", "1"),
        new("contact-multi-account", "Contact to multiple accounts", "1"),
        new("duplicate-merge", "Duplicate detection and merge", "1"),
        new("deal-line-items", "Deal product line items", "1"),
        new("deal-stage-gating", "Deal stage gating and validation", "1"),
        new("quick-log-dock", "One-click quick log", "1"),

        // Phase 2: platform and customization
        new("recycle-bin", "Recycle bin", "2"),
        new("dependent-picklists", "Dependent picklists", "2"),
        new("layout-rules", "Conditional field visibility", "2"),
        new("custom-objects", "Custom object builder", "2"),
        new("workflow-builder", "Visual workflow builder", "2"),
        new("scheduled-backups", "Scheduled backups", "2"),

        // Phase 3: sales and deal management
        new("deal-splits", "Deal revenue splits", "3"),
        new("forecasting", "Forecasting and weighted pipeline", "3"),
        new("fx-sync", "Live exchange rate sync", "3"),
        new("competitors", "Competitor tracking", "3"),
        new("win-loss", "Win/loss analysis", "3"),
        new("quote-esign", "Quote e-signature", "3"),
        new("payment-links", "Online payment links", "3"),

        // Phase 4: lead capture and automation
        new("cadences", "Sales cadences", "4"),
        new("lead-routing", "Round-robin and territory routing", "4"),
        new("lead-scoring", "Lead scoring", "4"),
        new("response-sla", "First-response SLA and escalation", "4"),
        new("visitor-tracking", "Website visitor tracking", "4"),
        new("email-template-builder", "Visual email template builder", "4"),
        new("unsubscribe-center", "GDPR and unsubscribe center", "4"),
        new("data-enrichment", "Automated data enrichment", "4"),

        // Phase 5: communication
        new("email-sync", "Two-way email sync", "5"),
        new("email-tracking", "Email open and click tracking", "5"),
        new("bcc-dropbox", "BCC dropbox", "5"),
        new("click-to-call", "Click-to-call", "5"),
        new("call-recording", "Call recording and transcription", "5"),
        new("whatsapp", "WhatsApp Business", "5"),
        new("shared-inbox", "Shared team inboxes", "5"),
        new("meeting-links", "Meeting booking links", "5"),

        // Phase 6: contact/account, analytics, mobile
        new("account-hierarchy", "Account hierarchy tree", "6"),
        new("customer-health", "Customer health, NPS and CSAT", "6"),
        new("linkedin-lookup", "LinkedIn profile lookup", "6"),
        new("dashboard-widgets", "Custom dashboard widgets", "6"),
        new("velocity-reports", "Velocity and funnel reports", "6"),
        new("leaderboards", "Leaderboards", "6"),
        new("scheduled-digests", "Scheduled email digests", "6"),
        new("pwa-offline", "Offline mode (PWA)", "6"),
    ];

    private static readonly HashSet<string> Keys = All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string? key) => key is not null && Keys.Contains(key);
}

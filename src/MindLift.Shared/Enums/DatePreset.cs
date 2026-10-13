namespace MindLift.Shared.Enums;

/// <summary>
/// Named date ranges resolved in the tenant's time zone by DateRangeResolver (plan section 4.7).
/// The same presets are used by list filters, dashboards and reports.
/// </summary>
public enum DatePreset
{
    Today,
    Yesterday,
    ThisWeek,
    LastWeek,
    ThisMonth,
    LastMonth,
    ThisQuarter,
    LastQuarter,
    ThisYear,
    LastYear,
    LastNDays,
    NextNDays,
}

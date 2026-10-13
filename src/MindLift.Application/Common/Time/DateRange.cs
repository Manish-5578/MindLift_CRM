namespace MindLift.Application.Common.Time;

/// <summary>A half-open UTC range: StartUtc is included, EndUtc is excluded.</summary>
public readonly record struct DateRange(DateTime StartUtc, DateTime EndUtc)
{
    public bool Contains(DateTime utc) => utc >= StartUtc && utc < EndUtc;
}

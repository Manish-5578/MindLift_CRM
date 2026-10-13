namespace MindLift.Application.Common.Time;

/// <summary>One definition of "a time zone the system can use", shared by company and branch settings.</summary>
public static class TimeZoneRules
{
    public static bool Exists(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

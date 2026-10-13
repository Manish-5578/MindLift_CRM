using System.Text;

namespace MindLift.Infrastructure.Activities;

/// <summary>Builds an iCalendar (.ics) invitation that Outlook, Google Calendar and Apple Calendar understand (RFC 5545).</summary>
public static class IcsBuilder
{
    public sealed record Meeting(
        Guid Id,
        string Subject,
        string? Description,
        string? Location,
        DateTime StartUtc,
        DateTime EndUtc,
        string OrganizerName,
        string OrganizerEmail,
        IReadOnlyList<string> Attendees,
        int Sequence);

    /// <summary>METHOD:REQUEST invites or updates, METHOD:CANCEL withdraws the meeting.</summary>
    public static string Build(Meeting meeting, bool cancel = false)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//MindLift//CRM//EN",
            "CALSCALE:GREGORIAN",
            cancel ? "METHOD:CANCEL" : "METHOD:REQUEST",
            "BEGIN:VEVENT",
            $"UID:{meeting.Id}@mindlift",
            $"DTSTAMP:{Stamp(DateTime.UtcNow)}",
            $"DTSTART:{Stamp(meeting.StartUtc)}",
            $"DTEND:{Stamp(meeting.EndUtc)}",
            $"SEQUENCE:{meeting.Sequence}",
            $"SUMMARY:{Escape(meeting.Subject)}",
            $"STATUS:{(cancel ? "CANCELLED" : "CONFIRMED")}",
            $"ORGANIZER;CN={Escape(meeting.OrganizerName)}:mailto:{meeting.OrganizerEmail}",
        };

        if (!string.IsNullOrWhiteSpace(meeting.Location))
        {
            lines.Add($"LOCATION:{Escape(meeting.Location)}");
        }

        if (!string.IsNullOrWhiteSpace(meeting.Description))
        {
            lines.Add($"DESCRIPTION:{Escape(meeting.Description)}");
        }

        lines.AddRange(meeting.Attendees.Select(a => $"ATTENDEE;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:{a}"));
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        return string.Join("\r\n", lines.SelectMany(Fold)) + "\r\n";
    }

    private static string Stamp(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Text values escape backslash, semicolon, comma and line breaks.</summary>
    private static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>Lines longer than 75 bytes continue on the next line, which starts with a space.</summary>
    private static IEnumerable<string> Fold(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length <= 75)
        {
            yield return line;
            yield break;
        }

        var builder = new StringBuilder();
        var size = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var runeSize = Encoding.UTF8.GetByteCount(rune.ToString());
            if (size + runeSize > limit)
            {
                yield return builder.ToString();
                builder.Clear().Append(' ');
                size = 1;
                limit = 75;
            }

            builder.Append(rune.ToString());
            size += runeSize;
        }

        yield return builder.ToString();
    }
}

using System.Text;

namespace MindLift.Shared.Imports;

/// <summary>Minimal RFC 4180 CSV reader and writer (quoted fields, doubled quotes, commas and line breaks inside quotes).</summary>
public static class CsvText
{
    /// <summary>
    /// Turns the bytes of an uploaded file into text. A byte-order mark decides first (UTF-8, UTF-16). Otherwise the bytes are read as UTF-8,
    /// and when they are not valid UTF-8 (a file saved by older Excel in Western European Windows) they are read as Latin-1 so accented letters survive.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    if (row.Any(f => f.Length > 0) || row.Count > 1) rows.Add(row);
                    row = new List<string>();
                    break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Quotes a value for CSV and stops spreadsheet formula injection: a cell that starts with = or @ (or a + or - that is not
    /// just a number or phone) would run as a formula when someone opens the export in Excel, so it gets a leading apostrophe.
    /// </summary>
    public static string Escape(string value)
    {
        value = NeutralizeFormula(value);
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static string NeutralizeFormula(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var first = value[0];
        if (first is '=' or '@' or '\t' or '\r')
        {
            return "'" + value;
        }

        // "+971 50 123 4567" and "-12.5" are plain numbers. "+cmd|..." and "-2+3+cmd" are not.
        if (first is '+' or '-' && !value.All(c => char.IsDigit(c) || c is ' ' or '(' or ')' or '+' or '-' or '.'))
        {
            return "'" + value;
        }

        return value;
    }
}


using System.Globalization;
using System.Text;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Reporting;

/// <summary>
/// Generates standards-compliant, publication-ready PDF reports from ReportResult data.
/// Supports multi-page pagination, auto-switching portrait/landscape layouts, branded headers,
/// column formatting, zebra rows, summary ribbons, and footer page numbering.
/// </summary>
public static class ReportPdfExporter
{
    public static byte[] ExportPdf(ReportResult result, string title)
    {
        var columns = result.Columns;
        var rows = result.Rows;

        // Auto-switch to landscape if wide table
        bool isLandscape = columns.Count > 6;
        float pageWidth = isLandscape ? 792f : 612f;
        float pageHeight = isLandscape ? 612f : 792f;

        float margin = 36f; // 0.5 inch margins
        float contentWidth = pageWidth - (margin * 2);

        // Calculate column widths proportional to content
        var colWidths = CalculateColumnWidths(columns, rows, contentWidth);

        // Prepare pages
        var pages = new List<string>();
        int currentRowIdx = 0;
        int totalRows = rows.Count;

        // Row height and page capacity
        float tableHeaderHeight = 24f;
        float rowHeight = 20f;
        float footerHeight = 30f;

        // Pass 1: Build page content streams
        int pageIndex = 1;
        while (currentRowIdx < totalRows || pageIndex == 1)
        {
            var sb = new StringBuilder();

            float currentY = pageHeight - margin;

            // Draw Header Block (Only on Page 1)
            if (pageIndex == 1)
            {
                // Top branded banner
                sb.AppendLine("q");
                sb.AppendLine("0.15 0.23 0.36 rg"); // Deep slate blue #273A5C
                sb.AppendLine($"{F(margin)} {F(currentY - 50)} {F(contentWidth)} 50 re f");
                sb.AppendLine("Q");

                // Banner text
                sb.AppendLine("BT");
                sb.AppendLine("/F2 14 Tf");
                sb.AppendLine("1 1 1 rg"); // White
                sb.AppendLine($"{F(margin + 14)} {F(currentY - 26)} Td");
                sb.AppendLine($"({EscapePdf(title)}) Tj");
                sb.AppendLine("ET");

                sb.AppendLine("BT");
                sb.AppendLine("/F1 8 Tf");
                sb.AppendLine("0.85 0.89 0.95 rg");
                sb.AppendLine($"{F(margin + 14)} {F(currentY - 40)} Td");
                sb.AppendLine($"({EscapePdf($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC | Rows: {result.RowCount:N0} | Execution: {result.ExecutionMs}ms")}) Tj");
                sb.AppendLine("ET");

                currentY -= 65f;

                // Summary / Totals Ribbon if present
                if (result.Totals != null && result.Totals.Count > 0)
                {
                    float ribbonHeight = 32f;
                    sb.AppendLine("q");
                    sb.AppendLine("0.96 0.97 0.99 rg");
                    sb.AppendLine("0.80 0.85 0.92 RG");
                    sb.AppendLine("0.75 w");
                    sb.AppendLine($"{F(margin)} {F(currentY - ribbonHeight)} {F(contentWidth)} {F(ribbonHeight)} re b");
                    sb.AppendLine("Q");

                    var firstTotals = result.Totals.Take(4).ToList();
                    float itemW = contentWidth / Math.Max(1, firstTotals.Count);

                    for (int i = 0; i < firstTotals.Count; i++)
                    {
                        var kv = firstTotals[i];
                        float itemX = margin + (i * itemW) + 10;
                        string keyText = kv.Key.Replace("_", " ").ToUpperInvariant();
                        string valText = kv.Value?.ToString() ?? "0";

                        sb.AppendLine("BT");
                        sb.AppendLine("/F2 7 Tf");
                        sb.AppendLine("0.4 0.45 0.55 rg");
                        sb.AppendLine($"{F(itemX)} {F(currentY - 14)} Td");
                        sb.AppendLine($"({EscapePdf(keyText)}) Tj");
                        sb.AppendLine("ET");

                        sb.AppendLine("BT");
                        sb.AppendLine("/F2 10 Tf");
                        sb.AppendLine("0.1 0.2 0.4 rg");
                        sb.AppendLine($"{F(itemX)} {F(currentY - 26)} Td");
                        sb.AppendLine($"({EscapePdf(valText)}) Tj");
                        sb.AppendLine("ET");
                    }

                    currentY -= ribbonHeight + 15f;
                }
                else
                {
                    currentY -= 10f;
                }
            }
            else
            {
                // Compact header on subsequent pages
                sb.AppendLine("BT");
                sb.AppendLine("/F2 9 Tf");
                sb.AppendLine("0.4 0.45 0.55 rg");
                sb.AppendLine($"{F(margin)} {F(currentY - 14)} Td");
                sb.AppendLine($"({EscapePdf($"{title} (continued)")}) Tj");
                sb.AppendLine("ET");

                currentY -= 25f;
            }

            // Draw Table Header
            sb.AppendLine("q");
            sb.AppendLine("0.25 0.35 0.50 rg"); // Header background
            sb.AppendLine($"{F(margin)} {F(currentY - tableHeaderHeight)} {F(contentWidth)} {F(tableHeaderHeight)} re f");
            sb.AppendLine("Q");

            float colX = margin;
            for (int c = 0; c < columns.Count; c++)
            {
                var col = columns[c];
                float w = colWidths[c];
                bool isNumeric = IsNumericType(col.Type);

                sb.AppendLine("BT");
                sb.AppendLine("/F2 8.5 Tf");
                sb.AppendLine("1 1 1 rg"); // White text

                string headerText = Truncate(col.Label, w, 8.5f);
                float textX = isNumeric ? colX + w - 8 - MeasureText(headerText, 8.5f) : colX + 8;
                sb.AppendLine($"{F(Math.Max(colX + 4, textX))} {F(currentY - 16)} Td");
                sb.AppendLine($"({EscapePdf(headerText)}) Tj");
                sb.AppendLine("ET");

                colX += w;
            }

            currentY -= tableHeaderHeight;

            // Table Data Rows
            float bottomLimit = margin + footerHeight;
            int pageRowCount = 0;

            while (currentRowIdx < totalRows && (currentY - rowHeight) >= bottomLimit)
            {
                var row = rows[currentRowIdx];
                bool isZebra = (pageRowCount % 2 == 1);

                if (isZebra)
                {
                    sb.AppendLine("q");
                    sb.AppendLine("0.97 0.98 1.0 rg"); // Subtle alternating zebra
                    sb.AppendLine($"{F(margin)} {F(currentY - rowHeight)} {F(contentWidth)} {F(rowHeight)} re f");
                    sb.AppendLine("Q");
                }

                // Bottom divider line
                sb.AppendLine("q");
                sb.AppendLine("0.90 0.92 0.95 RG");
                sb.AppendLine("0.5 w");
                sb.AppendLine($"{F(margin)} {F(currentY - rowHeight)} m {F(margin + contentWidth)} {F(currentY - rowHeight)} l S");
                sb.AppendLine("Q");

                colX = margin;
                for (int c = 0; c < columns.Count; c++)
                {
                    var col = columns[c];
                    float w = colWidths[c];
                    bool isNumeric = IsNumericType(col.Type);

                    string valStr = "";
                    if (row.TryGetValue(col.Key, out var rawVal) && rawVal != null)
                    {
                        valStr = FormatValue(rawVal, col.Type, col.Format);
                    }

                    sb.AppendLine("BT");
                    sb.AppendLine("/F1 8 Tf");
                    sb.AppendLine("0.15 0.18 0.22 rg"); // Dark charcoal text

                    string cellText = Truncate(valStr, w, 8f);
                    float textX = isNumeric ? colX + w - 8 - MeasureText(cellText, 8f) : colX + 8;
                    sb.AppendLine($"{F(Math.Max(colX + 4, textX))} {F(currentY - 14)} Td");
                    sb.AppendLine($"({EscapePdf(cellText)}) Tj");
                    sb.AppendLine("ET");

                    colX += w;
                }

                currentY -= rowHeight;
                currentRowIdx++;
                pageRowCount++;
            }

            // Totals row at the very end of data if it fits, or let it sit on the final page
            if (currentRowIdx >= totalRows && result.Totals != null && (currentY - rowHeight) >= bottomLimit)
            {
                sb.AppendLine("q");
                sb.AppendLine("0.92 0.95 0.99 rg");
                sb.AppendLine("0.70 0.78 0.90 RG");
                sb.AppendLine("0.75 w");
                sb.AppendLine($"{F(margin)} {F(currentY - rowHeight)} {F(contentWidth)} {F(rowHeight)} re b");
                sb.AppendLine("Q");

                colX = margin;
                for (int c = 0; c < columns.Count; c++)
                {
                    var col = columns[c];
                    float w = colWidths[c];
                    bool isNumeric = IsNumericType(col.Type);

                    string totalStr = "";
                    if (c == 0) totalStr = "TOTAL";
                    else if (result.Totals.TryGetValue(col.Key, out var tVal) && tVal != null)
                    {
                        totalStr = FormatValue(tVal, col.Type, col.Format);
                    }

                    if (!string.IsNullOrEmpty(totalStr))
                    {
                        sb.AppendLine("BT");
                        sb.AppendLine("/F2 8.5 Tf");
                        sb.AppendLine("0.1 0.2 0.4 rg");

                        string cellText = Truncate(totalStr, w, 8.5f);
                        float textX = isNumeric ? colX + w - 8 - MeasureText(cellText, 8.5f) : colX + 8;
                        sb.AppendLine($"{F(Math.Max(colX + 4, textX))} {F(currentY - 14)} Td");
                        sb.AppendLine($"({EscapePdf(cellText)}) Tj");
                        sb.AppendLine("ET");
                    }

                    colX += w;
                }

                currentY -= rowHeight;
            }

            // Footer
            float footerY = margin + 12f;
            sb.AppendLine("q");
            sb.AppendLine("0.85 0.88 0.92 RG");
            sb.AppendLine("0.5 w");
            sb.AppendLine($"{F(margin)} {F(footerY + 12)} m {F(margin + contentWidth)} {F(footerY + 12)} l S");
            sb.AppendLine("Q");

            sb.AppendLine("BT");
            sb.AppendLine("/F1 7.5 Tf");
            sb.AppendLine("0.5 0.55 0.6 rg");
            sb.AppendLine($"{F(margin)} {F(footerY)} Td");
            sb.AppendLine($"({EscapePdf("Generated by MindLift CRM Analytics · Confidential")}) Tj");
            sb.AppendLine("ET");

            // Page number placeholder will be replaced after total pages known
            sb.AppendLine($"%%PAGENUMBER_{pageIndex}%%");

            pages.Add(sb.ToString());
            pageIndex++;
        }

        int totalPages = pages.Count;

        // Pass 2: Assemble full PDF document
        return AssemblePdfDocument(pages, totalPages, pageWidth, pageHeight, margin, contentWidth);
    }

    private static byte[] AssemblePdfDocument(List<string> pageStreams, int totalPages, float pageWidth, float pageHeight, float margin, float contentWidth)
    {
        // PDF Object IDs:
        // 1: Catalog
        // 2: Pages container
        // 3: Font F1 (Helvetica)
        // 4: Font F2 (Helvetica-Bold)
        // 5.. : Page objects and content stream objects
        // For N pages:
        // Page 1: Obj 5, Content 1: Obj 6
        // Page 2: Obj 7, Content 2: Obj 8, etc.

        var offsets = new List<long>();
        var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Header
        writer.Write("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        writer.Flush();

        // Helper to record offset and write obj
        void BeginObj(int id)
        {
            writer.Flush();
            offsets.Add(ms.Position);
            writer.Write($"{id} 0 obj\n");
        }

        void EndObj()
        {
            writer.Write("endobj\n");
            writer.Flush();
        }

        // 1 0 obj: Catalog
        BeginObj(1);
        writer.Write("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObj();

        // 2 0 obj: Pages
        BeginObj(2);
        var kids = new StringBuilder();
        for (int p = 1; p <= totalPages; p++)
        {
            int pageObjId = 4 + (p * 2) - 1; // 5, 7, 9, ...
            kids.Append($"{pageObjId} 0 R ");
        }
        writer.Write($"<< /Type /Pages /Kids [ {kids} ] /Count {totalPages} >>\n");
        EndObj();

        // 3 0 obj: Font F1
        BeginObj(3);
        writer.Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\n");
        EndObj();

        // 4 0 obj: Font F2
        BeginObj(4);
        writer.Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\n");
        EndObj();

        // Write Pages and Streams
        for (int p = 1; p <= totalPages; p++)
        {
            int pageObjId = 4 + (p * 2) - 1;
            int streamObjId = pageObjId + 1;

            // Page Object
            BeginObj(pageObjId);
            writer.Write("<< /Type /Page /Parent 2 0 R ");
            writer.Write($"/MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] ");
            writer.Write("/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> ");
            writer.Write($"/Contents {streamObjId} 0 R >>\n");
            EndObj();

            // Stream Object
            string streamContent = pageStreams[p - 1];

            // Replace page number placeholder
            var footerY = margin + 12f;
            var pageNumText = $"Page {p} of {totalPages}";
            var pageNumX = margin + contentWidth - MeasureText(pageNumText, 7.5f);
            var pageNumStream = $"BT\n/F1 7.5 Tf\n0.5 0.55 0.6 rg\n{F(pageNumX)} {F(footerY)} Td\n({EscapePdf(pageNumText)}) Tj\nET\n";

            streamContent = streamContent.Replace($"%%PAGENUMBER_{p}%%", pageNumStream);
            var streamBytes = Encoding.Latin1.GetBytes(streamContent);

            BeginObj(streamObjId);
            writer.Write($"<< /Length {streamBytes.Length} >>\nstream\n");
            writer.Flush();
            ms.Write(streamBytes, 0, streamBytes.Length);
            writer.Write("\nendstream\n");
            EndObj();
        }

        // Cross-reference table
        writer.Flush();
        long xrefStart = ms.Position;
        int totalObjects = 4 + (totalPages * 2);

        writer.Write($"xref\n0 {totalObjects + 1}\n");
        writer.Write("0000000000 65535 f \n");
        for (int i = 0; i < offsets.Count; i++)
        {
            writer.Write($"{offsets[i]:D10} 00000 n \n");
        }

        // Trailer
        writer.Write("trailer\n");
        writer.Write($"<< /Size {totalObjects + 1} /Root 1 0 R >>\n");
        writer.Write("startxref\n");
        writer.Write($"{xrefStart}\n");
        writer.Write("%%EOF\n");
        writer.Flush();

        return ms.ToArray();
    }

    private static float[] CalculateColumnWidths(List<ReportColumn> columns, List<Dictionary<string, object?>> rows, float totalWidth)
    {
        int count = Math.Max(1, columns.Count);
        var weights = new float[count];

        for (int i = 0; i < count; i++)
        {
            var col = columns[i];
            float w = Math.Max(col.Label.Length, 6);

            if (col.Type == "Decimal" || col.Type == "Integer") w = Math.Max(w, 8);
            else if (col.Type == "Date" || col.Type == "DateTime") w = Math.Max(w, 10);
            else w = Math.Max(w, 14);

            weights[i] = w;
        }

        float totalWeight = weights.Sum();
        var widths = new float[count];
        for (int i = 0; i < count; i++)
        {
            widths[i] = (weights[i] / totalWeight) * totalWidth;
        }
        return widths;
    }

    private static bool IsNumericType(string type) =>
        type.Equals("Decimal", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Currency", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("Number", StringComparison.OrdinalIgnoreCase);

    private static string FormatValue(object val, string type, string? format)
    {
        if (val is null) return "";
        if (val is DateTime dt) return dt.ToString("yyyy-MM-dd");
        if (val is decimal dec) return dec.ToString("N2", CultureInfo.InvariantCulture);
        if (val is double dbl) return dbl.ToString("N2", CultureInfo.InvariantCulture);
        if (val is float flt) return flt.ToString("N2", CultureInfo.InvariantCulture);
        if (val is int or long or short) return string.Format(CultureInfo.InvariantCulture, "{0:N0}", val);
        return val.ToString() ?? "";
    }

    private static string EscapePdf(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '(': sb.Append(@"\("); break;
                case ')': sb.Append(@"\)"); break;
                case '\r': break;
                case '\n': sb.Append(' '); break;
                default:
                    if (c < 32 || c > 126)
                    {
                        // Map special characters or replace with safe ascii
                        if (c == '€') sb.Append("EUR");
                        else if (c == '£') sb.Append("GBP");
                        else if (c == '•') sb.Append("-");
                        else if (c == '—' || c == '–') sb.Append("-");
                        else sb.Append(' ');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static float MeasureText(string text, float fontSize)
    {
        // Average character width for Helvetica is ~0.52 * fontSize
        return text.Length * (fontSize * 0.52f);
    }

    private static string Truncate(string text, float availableWidth, float fontSize)
    {
        float maxChars = (availableWidth - 8f) / (fontSize * 0.52f);
        if (maxChars < 3) maxChars = 3;
        int limit = (int)maxChars;
        if (text.Length <= limit) return text;
        return text[..Math.Max(1, limit - 2)] + "..";
    }

    private static string F(float val) => val.ToString("0.##", CultureInfo.InvariantCulture);
}

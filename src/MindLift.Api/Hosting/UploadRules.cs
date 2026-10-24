namespace MindLift.Api.Hosting;

/// <summary>What a person may upload as a business document. Only kinds a company really exchanges; anything that could run in a browser is refused.</summary>
public static class UploadRules
{
    /// <summary>A little over the 25 MB file limit, for the form around the file.</summary>
    public const long RequestLimitBytes = 26 * 1024 * 1024;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp", ".txt", ".csv", ".doc", ".docx", ".xls", ".xlsx",
    };

    public static string AllowedList => string.Join(", ", Allowed.Order());

    public static bool IsAllowed(string extension) => Allowed.Contains(extension);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MindLift.Api.Hosting;

/// <summary>
/// Browser protections on every response: framing and MIME sniffing refused, a Content-Security-Policy, and (behind a TLS proxy) HTTPS enforced with HSTS.
/// <c>Security:CspMode</c> is <c>Enforce</c> (default outside Development), <c>ReportOnly</c> or <c>Off</c> (default in Development).
/// <c>Security:RequireHttps</c> (default on outside Development when the forwarded headers are trusted, that is when a proxy ends TLS) redirects plain HTTP and sends Strict-Transport-Security; it relies on the forwarded headers
/// being trusted (<c>ForwardedHeaders:Enabled</c>) when a proxy ends TLS in front of the app.
/// </summary>
public static partial class SecurityHeaders
{
    // Blazor WebAssembly needs 'wasm-unsafe-eval'. MudBlazor writes inline style attributes, so styles allow 'unsafe-inline'; scripts do not.
    // Fonts come from Google Fonts; SignalR uses same-origin WebSockets; charts and uploads preview use data: and blob: images.
    private const string Template =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'{0}; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self' ws: wss:; " +
        "worker-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseMindLiftSecurityHeaders(this WebApplication app)
    {
        var development = app.Environment.IsDevelopment();
        var mode = app.Configuration["Security:CspMode"] ?? (development ? "Off" : "Enforce");
        var requireHttps = app.Configuration.GetValue<bool?>("Security:RequireHttps") ?? (!development && ForwardedHeadersSetup.IsEnabled(app.Configuration));

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";
            var probe = path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
            if (requireHttps && !context.Request.IsHttps && !probe)
            {
                var target = $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                context.Response.Redirect(target, permanent: false, preserveMethod: true);
                return;
            }

            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (requireHttps && context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
            {
                headers["Content-Security-Policy"] = "frame-ancestors 'none'";
                await next();
                return;
            }

            var name = string.Equals(mode, "ReportOnly", StringComparison.OrdinalIgnoreCase) ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
            if (!IsPage(context))
            {
                headers[name] = string.Format(Template, string.Empty);
                await next();
                return;
            }

            // A page (the Blazor shell) carries one inline script, the import map Blazor fills in at publish. Its hash is read from the page as served
            // and allowed for that response alone, so scripts stay 'self' only. The page is small, so it is served whole (never compressed or 304).
            context.Request.Headers.Remove("Accept-Encoding");
            context.Request.Headers.Remove("If-None-Match");
            context.Request.Headers.Remove("If-Modified-Since");
            var original = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next();
            }
            finally
            {
                context.Response.Body = original;
            }

            var isHtml = context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
            headers[name] = string.Format(Template, isHtml ? InlineScriptHashes(Encoding.UTF8.GetString(buffer.ToArray())) : string.Empty);
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
        });
    }

    private const char Quote = (char)39;

    private static bool IsPage(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;
        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase)) return false;
        var last = path[(path.LastIndexOf('/') + 1)..];
        return !last.Contains('.') || last.Equals("index.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string InlineScriptHashes(string html)
    {
        var builder = new StringBuilder();
        foreach (Match m in InlineScript().Matches(html))
        {
            var body = m.Groups[1].Value;
            if (body.Length == 0) continue;
            // Browsers hash the text with line breaks normalised to LF.
            var normalised = body.Replace("\r\n", "\n").Replace("\r", "\n");
            builder.Append(" 'sha256-").Append(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))).Append(Quote);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}

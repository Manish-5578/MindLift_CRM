using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MindLift.Infrastructure.CalendarSync;

public sealed record TokenSet(string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc);

/// <summary>A meeting as another calendar needs it.</summary>
public sealed record ExternalEvent(string Subject, string? Description, string? Location, DateTime StartUtc, DateTime EndUtc);

/// <summary>A taken time in another calendar. Only the time and its id.</summary>
public sealed record ExternalBusy(string Id, DateTime StartUtc, DateTime EndUtc);

/// <summary>One calendar service (Google, Microsoft). Each speaks its own web API; the sync service only sees this.</summary>
public interface ICalendarProvider
{
    string Key { get; }
    string DisplayName { get; }

    /// <summary>False until the company's administrator has given the service's client id and secret in the settings.</summary>
    bool Configured { get; }

    string AuthorizeUrl(string state, string redirectUri);
    Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken);
    Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<string> AccountEmailAsync(string accessToken, CancellationToken cancellationToken);
    Task<string> CreateEventAsync(string accessToken, ExternalEvent e, CancellationToken cancellationToken);
    Task UpdateEventAsync(string accessToken, string id, ExternalEvent e, CancellationToken cancellationToken);
    Task DeleteEventAsync(string accessToken, string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExternalBusy>> BusyAsync(string accessToken, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}

public static class CalendarHttp
{
    public const string ClientName = "CalendarProviders";

    public static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CalendarProviderException((int)response.StatusCode, text.Length > 300 ? text[..300] : text);
        }

        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static DateTime Iso(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

public sealed class CalendarProviderException(int status, string message) : Exception($"The calendar service answered {status}: {message}")
{
    public int Status { get; } = status;

    /// <summary>The permission was taken back or the sign-in is no longer valid: the person has to connect again.</summary>
    public bool NeedsReconnect => Status is 400 or 401 or 403;
}

public sealed class GoogleCalendarProvider(IHttpClientFactory http, IConfiguration configuration) : ICalendarProvider
{
    public string Key => "google";
    public string DisplayName => "Google Calendar";
    private string ClientId => configuration["Calendar:Google:ClientId"] ?? string.Empty;
    private string ClientSecret => configuration["Calendar:Google:ClientSecret"] ?? string.Empty;
    public bool Configured => ClientId.Length > 0 && ClientSecret.Length > 0;

    private HttpClient Client => http.CreateClient(CalendarHttp.ClientName);

    public string AuthorizeUrl(string state, string redirectUri)
        => "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&access_type=offline&prompt=consent"
           + $"&client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}"
           + "&scope=" + Uri.EscapeDataString("openid email https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.readonly");

    public Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
        => TokenAsync(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri }, null, cancellationToken);

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, refreshToken, cancellationToken);

    private async Task<TokenSet> TokenAsync(Dictionary<string, string> form, string? keepRefresh, CancellationToken cancellationToken)
    {
        form["client_id"] = ClientId;
        form["client_secret"] = ClientSecret;
        var json = await CalendarHttp.ReadAsync(await Client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken), cancellationToken);
        return new TokenSet(json.GetProperty("access_token").GetString()!, json.TryGetProperty("refresh_token", out var r) ? r.GetString() : keepRefresh,
            DateTime.UtcNow.AddSeconds(json.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600));
    }

    public async Task<string> AccountEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        var json = await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo", accessToken), cancellationToken), cancellationToken);
        return json.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty;
    }

    private static object Body(ExternalEvent e) => new
    {
        summary = e.Subject, description = e.Description, location = e.Location,
        start = new { dateTime = e.StartUtc.ToString("o", CultureInfo.InvariantCulture), timeZone = "UTC" },
        end = new { dateTime = e.EndUtc.ToString("o", CultureInfo.InvariantCulture), timeZone = "UTC" },
        source = new { title = "MindLift", url = "https://mindlift.invalid" },
    };

    public async Task<string> CreateEventAsync(string accessToken, ExternalEvent e, CancellationToken cancellationToken)
    {
        var json = await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/calendars/primary/events", accessToken, JsonContent.Create(Body(e))), cancellationToken), cancellationToken);
        return json.GetProperty("id").GetString()!;
    }

    public async Task UpdateEventAsync(string accessToken, string id, ExternalEvent e, CancellationToken cancellationToken)
        => await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Put, $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{Uri.EscapeDataString(id)}", accessToken, JsonContent.Create(Body(e))), cancellationToken), cancellationToken);

    public async Task DeleteEventAsync(string accessToken, string id, CancellationToken cancellationToken)
    {
        var response = await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Delete, $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{Uri.EscapeDataString(id)}", accessToken), cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return;   // already gone
        await CalendarHttp.ReadAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalBusy>> BusyAsync(string accessToken, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var url = "https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&maxResults=250"
                  + $"&timeMin={Uri.EscapeDataString(fromUtc.ToString("o", CultureInfo.InvariantCulture))}&timeMax={Uri.EscapeDataString(toUtc.ToString("o", CultureInfo.InvariantCulture))}";
        var json = await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Get, url, accessToken), cancellationToken), cancellationToken);
        var busy = new List<ExternalBusy>();
        if (!json.TryGetProperty("items", out var items)) return busy;
        foreach (var item in items.EnumerateArray())
        {
            // Cancelled events and events marked "free" do not block time.
            if ((item.TryGetProperty("status", out var st) && st.GetString() == "cancelled") || (item.TryGetProperty("transparency", out var tr) && tr.GetString() == "transparent")) continue;
            var start = item.GetProperty("start");
            var end = item.GetProperty("end");
            var (s, e) = start.TryGetProperty("dateTime", out var sd) ? (CalendarHttp.Iso(sd.GetString()!), CalendarHttp.Iso(end.GetProperty("dateTime").GetString()!))
                : (CalendarHttp.Iso(start.GetProperty("date").GetString()!), CalendarHttp.Iso(end.GetProperty("date").GetString()!));   // an all-day event blocks the whole day
            busy.Add(new ExternalBusy(item.GetProperty("id").GetString()!, s, e));
        }

        return busy;
    }
}

public sealed class MicrosoftCalendarProvider(IHttpClientFactory http, IConfiguration configuration) : ICalendarProvider
{
    public string Key => "microsoft";
    public string DisplayName => "Outlook / Microsoft 365";
    private string ClientId => configuration["Calendar:Microsoft:ClientId"] ?? string.Empty;
    private string ClientSecret => configuration["Calendar:Microsoft:ClientSecret"] ?? string.Empty;
    private string Directory => configuration["Calendar:Microsoft:Tenant"] is { Length: > 0 } t ? t : "common";
    public bool Configured => ClientId.Length > 0 && ClientSecret.Length > 0;

    private const string Scope = "offline_access User.Read Calendars.ReadWrite";
    private HttpClient Client => http.CreateClient(CalendarHttp.ClientName);

    public string AuthorizeUrl(string state, string redirectUri)
        => $"https://login.microsoftonline.com/{Directory}/oauth2/v2.0/authorize?response_type=code&response_mode=query&prompt=select_account"
           + $"&client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(Scope)}";

    public Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
        => TokenAsync(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri }, null, cancellationToken);

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, refreshToken, cancellationToken);

    private async Task<TokenSet> TokenAsync(Dictionary<string, string> form, string? keepRefresh, CancellationToken cancellationToken)
    {
        form["client_id"] = ClientId;
        form["client_secret"] = ClientSecret;
        form["scope"] = Scope;
        var json = await CalendarHttp.ReadAsync(await Client.PostAsync($"https://login.microsoftonline.com/{Directory}/oauth2/v2.0/token", new FormUrlEncodedContent(form), cancellationToken), cancellationToken);
        return new TokenSet(json.GetProperty("access_token").GetString()!, json.TryGetProperty("refresh_token", out var r) ? r.GetString() : keepRefresh,
            DateTime.UtcNow.AddSeconds(json.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600));
    }

    public async Task<string> AccountEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        var json = await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName", accessToken), cancellationToken), cancellationToken);
        return (json.TryGetProperty("mail", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null)
            ?? (json.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null) ?? string.Empty;
    }

    private static object Body(ExternalEvent e) => new
    {
        subject = e.Subject,
        body = new { contentType = "text", content = e.Description ?? string.Empty },
        location = new { displayName = e.Location ?? string.Empty },
        start = new { dateTime = e.StartUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timeZone = "UTC" },
        end = new { dateTime = e.EndUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timeZone = "UTC" },
    };

    public async Task<string> CreateEventAsync(string accessToken, ExternalEvent e, CancellationToken cancellationToken)
    {
        var json = await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/events", accessToken, JsonContent.Create(Body(e))), cancellationToken), cancellationToken);
        return json.GetProperty("id").GetString()!;
    }

    public async Task UpdateEventAsync(string accessToken, string id, ExternalEvent e, CancellationToken cancellationToken)
        => await CalendarHttp.ReadAsync(await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/me/events/{Uri.EscapeDataString(id)}", accessToken, JsonContent.Create(Body(e))), cancellationToken), cancellationToken);

    public async Task DeleteEventAsync(string accessToken, string id, CancellationToken cancellationToken)
    {
        var response = await Client.SendAsync(CalendarHttp.Authorized(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/me/events/{Uri.EscapeDataString(id)}", accessToken), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        await CalendarHttp.ReadAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalBusy>> BusyAsync(string accessToken, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var url = "https://graph.microsoft.com/v1.0/me/calendarView?$select=id,start,end,showAs,isCancelled&$top=250"
                  + $"&startDateTime={Uri.EscapeDataString(fromUtc.ToString("o", CultureInfo.InvariantCulture))}&endDateTime={Uri.EscapeDataString(toUtc.ToString("o", CultureInfo.InvariantCulture))}";
        var request = CalendarHttp.Authorized(HttpMethod.Get, url, accessToken);
        request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
        var json = await CalendarHttp.ReadAsync(await Client.SendAsync(request, cancellationToken), cancellationToken);
        var busy = new List<ExternalBusy>();
        if (!json.TryGetProperty("value", out var items)) return busy;
        foreach (var item in items.EnumerateArray())
        {
            if ((item.TryGetProperty("isCancelled", out var c) && c.GetBoolean()) || (item.TryGetProperty("showAs", out var sa) && sa.GetString() is "free" or "workingElsewhere")) continue;
            busy.Add(new ExternalBusy(item.GetProperty("id").GetString()!, CalendarHttp.Iso(item.GetProperty("start").GetProperty("dateTime").GetString()!), CalendarHttp.Iso(item.GetProperty("end").GetProperty("dateTime").GetString()!)));
        }

        return busy;
    }
}

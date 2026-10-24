using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.CalendarSync;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Bookings;

/// <summary>
/// Google and Microsoft calendar sync, against a pretend calendar service: connecting, copying meetings out, reading taken times in,
/// and the .ics feed. Nothing here talks to the internet.
/// </summary>
public sealed class CalendarSyncTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    /// <summary>A stand-in for Google's web API that remembers what it was asked and answers like the real thing.</summary>
    private sealed class FakeGoogle : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Calls { get; } = [];
        public List<(string Id, DateTime Start, DateTime End)> Busy { get; } = [];
        public bool RevokeAccess { get; set; }
        private int _next = 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Calls) Calls.Add((request.Method, url, body));
            if (RevokeAccess && !url.Contains("oauth2.googleapis.com")) return Json(HttpStatusCode.Unauthorized, new { error = "invalid_grant" });

            if (url.StartsWith("https://oauth2.googleapis.com/token")) return Json(HttpStatusCode.OK, new { access_token = "access-" + _next++, refresh_token = "refresh-1", expires_in = 3600 });
            if (url.StartsWith("https://openidconnect.googleapis.com/v1/userinfo")) return Json(HttpStatusCode.OK, new { email = "dana@gmail.test" });
            if (request.Method == HttpMethod.Post && url.EndsWith("/events")) return Json(HttpStatusCode.OK, new { id = "ext-" + _next++ });
            if (request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete) return request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(HttpStatusCode.OK, new { });
            if (request.Method == HttpMethod.Get && url.Contains("/events?"))
            {
                return Json(HttpStatusCode.OK, new
                {
                    items = Busy.Select(b => new { id = b.Id, status = "confirmed", start = new { dateTime = b.Start.ToString("o") }, end = new { dateTime = b.End.ToString("o") } }).ToArray(),
                });
            }

            return Json(HttpStatusCode.NotFound, new { });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body)
            => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host, FakeGoogle Google) Host()
    {
        var google = new FakeGoogle();
        var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Calendar:Google:ClientId", "client-id");
            b.UseSetting("Calendar:Google:ClientSecret", "client-secret");
            b.UseSetting("App:BaseUrl", "http://localhost");
            b.ConfigureTestServices(s => s.AddHttpClient(CalendarHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => google));
        });
        return (host, google);
    }

    private static async Task ConnectAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, Session s)
    {
        var url = (await (await s.Client.PostAsync("/api/v1/calendar/sync/connect/google", null)).Content.ReadFromJsonAsync<CalendarConnectUrlDto>())!.Url;
        var state = Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Match(url, "state=([^&]+)").Groups[1].Value);
        var back = await host.CreateClient(new() { AllowAutoRedirect = false }).GetAsync($"/calendar/oauth/google/callback?code=abc&state={Uri.EscapeDataString(state)}");
        back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        back.Headers.Location!.ToString().ShouldContain("connected=google");
    }

    private static async Task<ActivityDto> MeetingAsync(Session s, string subject, DateTime start)
        => (await (await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Meeting", subject, "Agenda", null, start, start.AddHours(1), null, null, null, "Room 1", null, null, null, null, null, null))).Content.ReadFromJsonAsync<ActivityDto>())!;

    private static async Task SyncAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, Session s)
        => (await s.Client.PostAsync("/api/v1/calendar/sync/now", null)).EnsureSuccessStatusCode();

    [Fact]
    public async Task A_service_is_offered_only_when_it_has_been_set_up_and_the_connect_link_carries_a_signed_state()
    {
        var (host, _) = Host();
        var s = await Workspaces.SignupAsync(host);
        var status = (await s.Client.GetFromJsonAsync<CalendarSyncDto>("/api/v1/calendar/sync"))!;
        status.Providers.Single(p => p.Key == "google").Configured.ShouldBeTrue();
        status.Providers.Single(p => p.Key == "microsoft").Configured.ShouldBeFalse();
        (await s.Client.PostAsync("/api/v1/calendar/sync/connect/microsoft", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var url = (await (await s.Client.PostAsync("/api/v1/calendar/sync/connect/google", null)).Content.ReadFromJsonAsync<CalendarConnectUrlDto>())!.Url;
        url.ShouldStartWith("https://accounts.google.com/o/oauth2/v2/auth");
        url.ShouldContain("client_id=client-id");
        url.ShouldContain("access_type=offline");

        // A made-up or tampered state connects nothing.
        var anonymous = host.CreateClient(new() { AllowAutoRedirect = false });
        (await anonymous.GetAsync("/calendar/oauth/google/callback?code=abc&state=forged")).Headers.Location!.ToString().ShouldContain("failed=google");
        (await s.Client.GetFromJsonAsync<CalendarSyncDto>("/api/v1/calendar/sync"))!.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Meetings_are_copied_to_the_other_calendar_changed_there_and_removed_when_they_are_done()
    {
        var (host, google) = Host();
        var s = await Workspaces.SignupAsync(host);
        await ConnectAsync(host, s);
        var connection = (await s.Client.GetFromJsonAsync<CalendarSyncDto>("/api/v1/calendar/sync"))!.Connections.Single();
        connection.AccountEmail.ShouldBe("dana@gmail.test");

        var meeting = await MeetingAsync(s, "Kick-off", DateTime.UtcNow.AddDays(2));
        await SyncAsync(host, s);
        google.Calls.Count(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/events")).ShouldBe(1);
        google.Calls.Last(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/events")).Body.ShouldContain("Kick-off");

        // Nothing changed, nothing is sent again.
        await SyncAsync(host, s);
        google.Calls.Count(c => c.Method == HttpMethod.Post && c.Url.EndsWith("/events")).ShouldBe(1);
        google.Calls.Count(c => c.Method == HttpMethod.Put).ShouldBe(0);

        // A change is sent as an update, not a second event.
        (await s.Client.PutAsJsonAsync($"/api/v1/activities/{meeting.Id}", new SaveActivityRequest("Meeting", "Kick-off (moved)", "Agenda", null, meeting.DueAtUtc, meeting.DueAtUtc!.Value.AddHours(1), null, null, null, "Room 2", null, null, null, null, null, null))).EnsureSuccessStatusCode();
        await SyncAsync(host, s);
        google.Calls.Count(c => c.Method == HttpMethod.Put).ShouldBe(1);
        google.Calls.Last(c => c.Method == HttpMethod.Put).Body.ShouldContain("moved");

        // A finished meeting leaves the other calendar.
        (await s.Client.PostAsJsonAsync($"/api/v1/activities/{meeting.Id}/complete", new CompleteActivityRequest(null, null, null))).EnsureSuccessStatusCode();
        await SyncAsync(host, s);
        google.Calls.Count(c => c.Method == HttpMethod.Delete).ShouldBe(1);

        (await s.Client.GetFromJsonAsync<CalendarSyncDto>("/api/v1/calendar/sync"))!.Connections.Single().LastSyncAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Times_taken_in_the_other_calendar_block_booking_and_show_as_busy_but_the_event_itself_is_not_copied()
    {
        var (host, google) = Host();
        var s = await Workspaces.SignupAsync(host);
        var me = (await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;
        var day = DateTime.UtcNow.Date.AddDays(1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);

        var hours = new AvailabilityDto("UTC", new Dictionary<string, IReadOnlyList<TimeRangeDto>>
        {
            ["mon"] = [new("09:00", "17:00")], ["tue"] = [new("09:00", "17:00")], ["wed"] = [new("09:00", "17:00")], ["thu"] = [new("09:00", "17:00")], ["fri"] = [new("09:00", "17:00")], ["sat"] = [], ["sun"] = [],
        }, []);
        var page = (await (await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", new SaveBookingPageRequest($"sync-{Guid.NewGuid():N}"[..14], "Call", null, 30, 0, 0, 30, 1, 30, "SingleUser", [me], hours,
            [], "Video", "https://meet.example.test/x", [60], true))).Content.ReadFromJsonAsync<BookingPageDto>())!;
        var parts = page.PublicPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var slotsUrl = $"/api/v1/public/booking/{parts[1]}/{parts[2]}/slots?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&tz=UTC";
        var anonymous = host.CreateClient();

        var before = (await anonymous.GetFromJsonAsync<List<SlotDto>>(slotsUrl))!;
        var taken = before.Single(x => x.StartUtc == day.AddHours(10));

        await ConnectAsync(host, s);
        google.Busy.Add(("their-meeting", day.AddHours(10), day.AddHours(11)));
        await SyncAsync(host, s);

        var after = (await anonymous.GetFromJsonAsync<List<SlotDto>>(slotsUrl))!;
        after.ShouldNotContain(x => x.StartUtc == day.AddHours(10));
        after.ShouldNotContain(x => x.StartUtc == day.AddHours(10).AddMinutes(30));
        after.ShouldContain(x => x.StartUtc == day.AddHours(11));
        taken.StartUtc.ShouldBe(day.AddHours(10));

        var events = (await s.Client.GetFromJsonAsync<List<CalendarEventDto>>($"/api/v1/calendar/events?from={day:yyyy-MM-dd}T00:00:00Z&to={day.AddDays(1):yyyy-MM-dd}T00:00:00Z"))!;
        var busy = events.Single(e => e.Kind == "external");
        busy.Title.ShouldBe("Busy (other calendar)");

        // Only the time was kept: nothing about the event is stored.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ExternalBusyBlocks.IgnoreQueryFilters().CountAsync(b => b.UserId == me)).ShouldBe(1);
    }

    [Fact]
    public async Task A_connection_whose_permission_was_taken_back_says_so_and_disconnecting_removes_the_copies()
    {
        var (host, google) = Host();
        var s = await Workspaces.SignupAsync(host);
        await ConnectAsync(host, s);
        await MeetingAsync(s, "To remove", DateTime.UtcNow.AddDays(3));
        await SyncAsync(host, s);

        google.RevokeAccess = true;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.CalendarConnections.IgnoreQueryFilters().ExecuteUpdateAsync(u => u.SetProperty(c => c.AccessExpiresAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        await SyncAsync(host, s);
        var failed = (await s.Client.GetFromJsonAsync<CalendarSyncDto>("/api/v1/calendar/sync"))!.Connections.Single();
        failed.LastError.ShouldNotBeNull();

        google.RevokeAccess = false;
        (await s.Client.DeleteAsync($"/api/v1/calendar/sync/connections/{failed.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Client.GetFromJsonAsync<CalendarSyncDto>("/api/v1/calendar/sync"))!.Connections.ShouldBeEmpty();
        google.Calls.ShouldContain(c => c.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task The_calendar_feed_lists_the_persons_meetings_for_any_calendar_app_and_a_new_address_stops_the_old_one()
    {
        var s = await Workspaces.SignupAsync(factory);
        await MeetingAsync(s, "Board; review, Q4", DateTime.UtcNow.AddDays(4));
        var other = await Workspaces.SignupAsync(factory);
        await MeetingAsync(other, "Someone else's meeting", DateTime.UtcNow.AddDays(4));

        var first = (await (await s.Client.PostAsync("/api/v1/calendar/sync/feed", null)).Content.ReadFromJsonAsync<CalendarFeedDto>())!.Url;
        var path = new Uri(first).AbsolutePath;
        path.ShouldEndWith(".ics");

        var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(path);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/calendar");
        var ics = await response.Content.ReadAsStringAsync();
        ics.ShouldContain("BEGIN:VCALENDAR");
        ics.ShouldContain("SUMMARY:Board\\; review\\, Q4");
        ics.ShouldNotContain("Someone else");
        ics.Split("\r\n").ShouldAllBe(l => Encoding.UTF8.GetByteCount(l) <= 75);

        var second = (await (await s.Client.PostAsync("/api/v1/calendar/sync/feed", null)).Content.ReadFromJsonAsync<CalendarFeedDto>())!.Url;
        second.ShouldNotBe(first);
        (await anonymous.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(new Uri(second).AbsolutePath)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await s.Client.DeleteAsync("/api/v1/calendar/sync/feed")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync(new Uri(second).AbsolutePath)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

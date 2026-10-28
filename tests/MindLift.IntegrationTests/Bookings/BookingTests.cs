using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Bookings;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Bookings;

/// <summary>Booking pages (plan 7.9): settings, free times, booking, double-booking protection, cancel, reschedule, reminders and the calendar.</summary>
public sealed class BookingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static AvailabilityDto Hours(string zone = "UTC") => new(zone,
        new Dictionary<string, IReadOnlyList<TimeRangeDto>>
        {
            ["mon"] = [new("09:00", "17:00")], ["tue"] = [new("09:00", "17:00")], ["wed"] = [new("09:00", "17:00")],
            ["thu"] = [new("09:00", "17:00")], ["fri"] = [new("09:00", "17:00")], ["sat"] = [], ["sun"] = [],
        },
        []);

    private static SaveBookingPageRequest Page(string slug, IReadOnlyList<Guid> users, string mode = "SingleUser", AvailabilityDto? hours = null)
        => new(slug, "Discovery call", "A short call", 30, 0, 0, 30, 1, 30, mode, users, hours ?? Hours(),
            [new("company", "Company", false, "text")], "Video", "https://meet.example.test/room", [60, 15], true);

    private static async Task<T> SendAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> IdAsync(Session s) => (await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

    private static async Task<BookingPageDto> CreatePageAsync(Session s, SaveBookingPageRequest request)
        => await SendAsync<BookingPageDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", request));

    /// <summary>The public path is /book/{workspace}/{page}. The workspace and page parts are used for the public calls.</summary>
    private static string PublicBase(BookingPageDto page)
    {
        var parts = page.PublicPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return $"/api/v1/public/booking/{parts[1]}/{parts[2]}";
    }

    /// <summary>The first Monday to Friday on or after today plus the given days. The pages are open on weekdays only.</summary>
    private static DateOnly Day(int days)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(days));
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private static string Range(int days) => $"{Day(days):yyyy-MM-dd}T00:00:00Z";

    private static async Task<List<SlotDto>> SlotsAsync(HttpClient anonymous, BookingPageDto page, DateOnly from, DateOnly to)
        => (await anonymous.GetFromJsonAsync<List<SlotDto>>($"{PublicBase(page)}/slots?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&tz=UTC"))!;

    private static BookRequest Request(DateTime start, string name = "Customer", string? email = null)
        => new(start, name, email ?? $"c-{Guid.NewGuid():N}@example.test", null, new Dictionary<string, string> { ["company"] = "Acme" }, "UTC", null);

    // ---------- settings ----------

    [Fact]
    public async Task A_booking_page_is_saved_with_its_hours_and_listed_with_its_public_address()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);

        var page = await CreatePageAsync(s, Page("discovery", [me]));

        page.Slug.ShouldBe("discovery");
        page.Availability.Weekly["mon"].Single().From.ShouldBe("09:00");
        page.Availability.Weekly.Count.ShouldBe(7);
        page.PublicPath.ShouldStartWith("/book/");
        page.PublicPath.ShouldEndWith("/discovery");
        (await s.Client.GetFromJsonAsync<List<BookingPageDto>>("/api/v1/settings/booking-pages"))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_booking_page_with_bad_settings_is_refused()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var overlapping = Hours() with { Weekly = new Dictionary<string, IReadOnlyList<TimeRangeDto>> { ["mon"] = [new("09:00", "13:00"), new("12:00", "17:00")] } };

        (await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", Page("Bad Slug!", [me]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", Page("overlap", [me], hours: overlapping))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", Page("nobody", []))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", Page("no-tz", [me], hours: Hours("Mars/Olympus")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await CreatePageAsync(s, Page("taken", [me]));
        (await s.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", Page("taken", [me]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- free times ----------

    [Fact]
    public async Task Free_times_follow_the_opening_hours_and_skip_meetings_already_in_the_diary()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("hours", [me]));
        var anonymous = factory.CreateClient();

        var first = await SlotsAsync(anonymous, page, Day(1), Day(1));
        first.Count.ShouldBeGreaterThan(0);
        first.All(x => x.StartUtc.Hour >= 9 && x.EndUtc.Hour <= 17).ShouldBeTrue();

        var busyStart = first[2].StartUtc;
        await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Meeting", "Blocked", null, null, busyStart, busyStart.AddMinutes(30), null, null, null, null, null, null, null, null, null, null));

        (await SlotsAsync(anonymous, page, Day(1), Day(1))).ShouldNotContain(x => x.StartUtc == busyStart);
    }

    [Fact]
    public async Task A_date_exception_can_close_a_day_that_is_normally_open()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var hours = Hours() with { DateOverrides = [new(Day(2).ToString("yyyy-MM-dd"), [])] };
        var page = await CreatePageAsync(s, Page("closed", [me], hours: hours));
        var anonymous = factory.CreateClient();

        (await SlotsAsync(anonymous, page, Day(2), Day(2))).ShouldBeEmpty();
        (await SlotsAsync(anonymous, page, Day(4), Day(4))).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_round_robin_page_offers_a_time_while_any_one_person_is_free()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.InviteAndAcceptAsync(factory, a, "Staff", "rr");
        var idA = await IdAsync(a);
        var idB = await IdAsync(b);
        var page = await CreatePageAsync(a, Page("team", [idA, idB], mode: "RoundRobin"));
        var anonymous = factory.CreateClient();
        var slot = (await SlotsAsync(anonymous, page, Day(1), Day(1)))[0].StartUtc;

        await a.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Meeting", "A busy", null, null, slot, slot.AddMinutes(30), null, null, null, null, null, null, null, null, null, idA));
        (await SlotsAsync(anonymous, page, Day(1), Day(1))).ShouldContain(x => x.StartUtc == slot);

        await b.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Meeting", "B busy", null, null, slot, slot.AddMinutes(30), null, null, null, null, null, null, null, null, null, idB));
        (await SlotsAsync(anonymous, page, Day(1), Day(1))).ShouldNotContain(x => x.StartUtc == slot);
    }

    [Fact]
    public async Task An_inactive_page_and_an_unknown_address_are_not_found()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("off", [me]) with { IsActive = false });
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(PublicBase(page))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/v1/public/booking/nowhere-co/nothing")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- booking ----------

    [Fact]
    public async Task A_booking_creates_a_meeting_a_lead_and_emails_a_calendar_invitation()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("book-me", [me]));
        var anonymous = factory.CreateClient();
        var slot = (await SlotsAsync(anonymous, page, Day(1), Day(1)))[1];
        var email = $"buyer-{Guid.NewGuid():N}@example.test";

        var booked = await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc, "Buyer Bo", email));
        var detail = await booked.Content.ReadAsStringAsync();
        booked.StatusCode.ShouldBe(HttpStatusCode.OK, detail);
        var confirmation = (await booked.Content.ReadFromJsonAsync<BookingConfirmationDto>())!;

        confirmation.StartUtc.ShouldBe(slot.StartUtc);
        confirmation.ManageToken.Length.ShouldBeGreaterThan(20);
        var events = (await s.Client.GetFromJsonAsync<List<CalendarEventDto>>($"/api/v1/calendar/events?from={Range(0)}&to={Range(8)}"))!;
        events.Single(e => e.Kind == "booking").Title.ShouldBe("Discovery call: Buyer Bo");
        var leads = await SendAsync<PagedResult<LeadListItemDto>>(await s.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(email, null, null, null)));
        leads.Items.Count.ShouldBe(1);

        var mail = factory.Emails.Sent.Single(m => m.To == email && m.Subject.Contains("Discovery call"));
        mail.Attachments!.Single().ContentType.ShouldContain("REQUEST");
        mail.Body.ShouldContain("/booking/manage/" + confirmation.ManageToken);
    }

    [Fact]
    public async Task The_same_time_cannot_be_booked_twice_and_disappears_from_the_free_times()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("once", [me]));
        var anonymous = factory.CreateClient();
        var slot = (await SlotsAsync(anonymous, page, Day(1), Day(1)))[0];

        await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc)));
        var second = await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SlotsAsync(anonymous, page, Day(1), Day(1))).ShouldNotContain(x => x.StartUtc == slot.StartUtc);
    }

    [Fact]
    public async Task A_booking_without_a_required_answer_or_with_a_bad_email_is_refused()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("needy", [me]) with { Questions = [new("company", "Company", true, "text")] });
        var anonymous = factory.CreateClient();
        var slot = (await SlotsAsync(anonymous, page, Day(1), Day(1)))[0];

        (await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc) with { Answers = new Dictionary<string, string>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc) with { Email = "not-an-email" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_filled_in_honeypot_looks_like_success_but_books_nothing()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("trap", [me]));
        var anonymous = factory.CreateClient();
        var slot = (await SlotsAsync(anonymous, page, Day(1), Day(1)))[0];

        await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc) with { Hp = "gotcha" }));

        (await SlotsAsync(anonymous, page, Day(1), Day(1))).ShouldContain(x => x.StartUtc == slot.StartUtc);
    }

    [Fact]
    public async Task A_customer_can_cancel_their_booking_and_the_time_is_offered_again()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("cancel-me", [me]));
        var anonymous = factory.CreateClient();
        var slot = (await SlotsAsync(anonymous, page, Day(1), Day(1)))[0];
        var confirmation = await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(slot.StartUtc)));

        var manage = await SendAsync<ManageBookingDto>(await anonymous.GetAsync($"/api/v1/public/booking/manage/{confirmation.ManageToken}"));
        manage.CanChange.ShouldBeTrue();
        (await anonymous.PostAsJsonAsync($"/api/v1/public/booking/manage/{confirmation.ManageToken}/cancel", new CancelBookingRequest("Can't make it"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SlotsAsync(anonymous, page, Day(1), Day(1))).ShouldContain(x => x.StartUtc == slot.StartUtc);
        (await anonymous.PostAsJsonAsync($"/api/v1/public/booking/manage/{confirmation.ManageToken}/cancel", new CancelBookingRequest(null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.GetFromJsonAsync<List<CalendarEventDto>>($"/api/v1/calendar/events?from={Range(0)}&to={Range(8)}"))!.ShouldNotContain(e => e.Kind == "booking");
    }

    [Fact]
    public async Task A_customer_can_move_their_booking_to_another_free_time_and_the_old_one_frees_up()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("move-me", [me]));
        var anonymous = factory.CreateClient();
        var free = await SlotsAsync(anonymous, page, Day(1), Day(1));
        var original = free[0].StartUtc;
        var target = free[3].StartUtc;
        var confirmation = await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(original)));

        var moved = await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"/api/v1/public/booking/manage/{confirmation.ManageToken}/reschedule", new RescheduleBookingRequest(target, "UTC")));

        moved.StartUtc.ShouldBe(target);
        var slots = await SlotsAsync(anonymous, page, Day(1), Day(1));
        slots.ShouldContain(x => x.StartUtc == original);
        slots.ShouldNotContain(x => x.StartUtc == target);
    }

    [Fact]
    public async Task Moving_a_booking_on_the_calendar_moves_the_customers_time_with_it()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("drag-me", [me]));
        var anonymous = factory.CreateClient();
        var free = await SlotsAsync(anonymous, page, Day(1), Day(1));
        var confirmation = await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(free[0].StartUtc)));
        var meeting = (await s.Client.GetFromJsonAsync<List<CalendarEventDto>>($"/api/v1/calendar/events?from={Range(0)}&to={Range(8)}"))!.Single(e => e.Kind == "booking");

        (await s.Client.PatchAsJsonAsync($"/api/v1/activities/{meeting.Id}/schedule", new ScheduleActivityRequest(free[4].StartUtc, null))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var manage = await SendAsync<ManageBookingDto>(await anonymous.GetAsync($"/api/v1/public/booking/manage/{confirmation.ManageToken}"));
        manage.StartUtc.ShouldBe(free[4].StartUtc);
    }

    [Fact]
    public async Task A_booking_notifies_the_person_and_a_reminder_goes_out_only_once()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("remind", [me]));
        var anonymous = factory.CreateClient();
        var free = await SlotsAsync(anonymous, page, Day(1), Day(1));
        var email = $"reminded-{Guid.NewGuid():N}@example.test";
        await SendAsync<BookingConfirmationDto>(await anonymous.PostAsJsonAsync($"{PublicBase(page)}/book", Request(free[0].StartUtc, "Remy", email)));

        (await s.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications"))!.Items.ShouldContain(n => n.Type == NotificationTypes.BookingCreated);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE bookings SET start_at = now() + interval '50 minutes' WHERE email = {0}", email);
        var job = scope.ServiceProvider.GetRequiredService<BookingReminderJob>();
        var first = await job.RunAsync();
        var second = await job.RunAsync();

        first.ShouldBeGreaterThanOrEqualTo(1);
        second.ShouldBe(0);
        factory.Emails.Sent.Count(m => m.To == email && m.Subject.StartsWith("Reminder:")).ShouldBe(1);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_these_booking_pages_or_their_bookings()
    {
        var s = await Workspaces.SignupAsync(factory);
        var me = await IdAsync(s);
        var page = await CreatePageAsync(s, Page("private", [me]));
        var stranger = await Workspaces.SignupAsync(factory);

        (await stranger.Client.GetAsync($"/api/v1/settings/booking-pages/{page.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.GetAsync($"/api/v1/settings/booking-pages/{page.Id}/bookings")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

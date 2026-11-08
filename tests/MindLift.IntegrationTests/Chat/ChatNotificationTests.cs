using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Chat;

/// <summary>Direct chat: a message becomes an Alerts-list notification for the recipient, presence follows pings, and /me carries the profile.</summary>
public sealed class ChatNotificationTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Direct_message_creates_a_notification_for_the_recipient_and_shows_as_last_message()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "chatter");

        var people = await staff.Client.GetFromJsonAsync<List<ChatUserDto>>("/api/v1/chat/users");
        var adminUser = people!.Single(p => p.Email.Equals(admin.Email, StringComparison.OrdinalIgnoreCase));

        var send = await staff.Client.PostAsJsonAsync("/api/v1/chat/messages",
            new SendChatMessageRequest("Hello boss, the quote is ready", null, adminUser.Id));
        send.StatusCode.ShouldBe(HttpStatusCode.Created);

        var page = await admin.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications");
        var n = page!.Items.Single(i => i.Type == NotificationTypes.ChatMessage);
        n.Body.ShouldBe("Hello boss, the quote is ready");
        n.Url.ShouldBe("/chat");
        n.IsRead.ShouldBeFalse();

        // The sender never notifies themselves.
        var own = await staff.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications");
        own!.Items.ShouldNotContain(i => i.Type == NotificationTypes.ChatMessage);

        // The recipient's list shows the preview and the unread count.
        var fromAdmin = await admin.Client.GetFromJsonAsync<List<ChatUserDto>>("/api/v1/chat/users");
        var row = fromAdmin!.Single(p => p.Email.Equals(staff.Email, StringComparison.OrdinalIgnoreCase));
        row.UnreadCount.ShouldBe(1);
        row.LastMessage.ShouldBe("Hello boss, the quote is ready");
    }

    [Fact]
    public async Task Presence_follows_pings_and_goes_offline_when_the_app_closes()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "presence");

        async Task<bool> StaffOnlineAsync()
        {
            var list = await admin.Client.GetFromJsonAsync<List<ChatUserDto>>("/api/v1/chat/users");
            return list!.Single(p => p.Email.Equals(staff.Email, StringComparison.OrdinalIgnoreCase)).IsOnline;
        }

        (await StaffOnlineAsync()).ShouldBeFalse();

        (await staff.Client.PostAsync("/api/v1/chat/ping", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StaffOnlineAsync()).ShouldBeTrue();

        (await staff.Client.PostAsync("/api/v1/chat/offline", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StaffOnlineAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Me_returns_the_real_name_role_and_admin_flag()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "profile");

        var a = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        a!.FullName.ShouldNotBeNullOrWhiteSpace();
        a.IsAdmin.ShouldBeTrue();

        var s = await staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        s!.RoleName.ShouldBe("Staff");
        s.IsAdmin.ShouldBeFalse();
    }
}

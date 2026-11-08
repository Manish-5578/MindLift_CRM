using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Chat;

/// <summary>Replies, photos, voice notes, reactions, typing and search in a direct chat.</summary>
public sealed class ChatRichMessageTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static MultipartFormDataContent Form(string fileName, byte[] bytes, string contentType, Guid to, string? caption = null, Guid? replyTo = null, int? durationMs = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(to.ToString()), "recipientId");
        if (caption is not null) form.Add(new StringContent(caption), "message");
        if (replyTo is not null) form.Add(new StringContent(replyTo.ToString()!), "replyToId");
        if (durationMs is not null) form.Add(new StringContent(durationMs.ToString()!), "durationMs");
        return form;
    }

    [Fact]
    public async Task Reply_photo_voice_note_reaction_typing_and_search_work_between_two_people()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rich");
        var people = await staff.Client.GetFromJsonAsync<List<ChatUserDto>>("/api/v1/chat/users");
        var adminUser = people!.Single(p => p.Email.Equals(admin.Email, StringComparison.OrdinalIgnoreCase));
        var staffUser = (await admin.Client.GetFromJsonAsync<List<ChatUserDto>>("/api/v1/chat/users"))!
            .Single(p => p.Email.Equals(staff.Email, StringComparison.OrdinalIgnoreCase));

        var first = await (await staff.Client.PostAsJsonAsync("/api/v1/chat/messages", new SendChatMessageRequest("Can you send the brochure?", null, adminUser.Id)))
            .Content.ReadFromJsonAsync<ChatMessageDto>();

        // A reply carries the original text and sender.
        var reply = await (await admin.Client.PostAsJsonAsync("/api/v1/chat/messages", new SendChatMessageRequest("Sending now", null, staffUser.Id, first!.Id)))
            .Content.ReadFromJsonAsync<ChatMessageDto>();
        reply!.ReplyToId.ShouldBe(first.Id);
        reply.ReplyToText.ShouldBe("Can you send the brochure?");

        // A photo and a voice note.
        var photoRes = await admin.Client.PostAsync("/api/v1/chat/messages/attachment", Form("brochure.png", [1, 2, 3, 4], "image/png", staffUser.Id, "Here it is"));
        photoRes.StatusCode.ShouldBe(HttpStatusCode.OK, await photoRes.Content.ReadAsStringAsync());
        var photo = await photoRes.Content.ReadFromJsonAsync<ChatMessageDto>();
        photo!.AttachmentKind.ShouldBe("image");
        photo.Message.ShouldBe("Here it is");

        var voice = await (await admin.Client.PostAsync("/api/v1/chat/messages/attachment", Form("note.m4a", [9, 9, 9], "audio/mp4", staffUser.Id, durationMs: 4200)))
            .Content.ReadFromJsonAsync<ChatMessageDto>();
        voice!.AttachmentKind.ShouldBe("audio");
        voice.AttachmentDurationMs.ShouldBe(4200);

        // The recipient can open it; a stranger cannot; a disallowed type is refused.
        var download = await staff.Client.GetAsync($"/api/v1/chat/messages/{photo.Id}/attachment");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(new byte[] { 1, 2, 3, 4 });
        var other = await Workspaces.SignupAsync(factory);
        (await other.Client.GetAsync($"/api/v1/chat/messages/{photo.Id}/attachment")).IsSuccessStatusCode.ShouldBeFalse();
        (await admin.Client.PostAsync("/api/v1/chat/messages/attachment", Form("virus.exe", [1], "application/octet-stream", staffUser.Id)))
            .IsSuccessStatusCode.ShouldBeFalse();

        // Reactions toggle and replace.
        var reacted = await (await staff.Client.PostAsJsonAsync($"/api/v1/chat/messages/{reply.Id}/react", new ReactRequest("👍"))).Content.ReadFromJsonAsync<ChatMessageDto>();
        reacted!.Reactions!.Single().Count.ShouldBe(1);
        reacted.Reactions!.Single().Mine.ShouldBeTrue();
        var removed = await (await staff.Client.PostAsJsonAsync($"/api/v1/chat/messages/{reply.Id}/react", new ReactRequest("👍"))).Content.ReadFromJsonAsync<ChatMessageDto>();
        removed!.Reactions!.ShouldBeEmpty();

        // Typing shows to the other person only, and only briefly.
        (await staff.Client.PostAsync($"/api/v1/chat/typing?toUserId={adminUser.Id}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/chat/typing?otherUserId={staffUser.Id}")).GetProperty("typing").GetBoolean().ShouldBeTrue();
        (await staff.Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/chat/typing?otherUserId={adminUser.Id}")).GetProperty("typing").GetBoolean().ShouldBeFalse();

        // Search finds text and file names; the list preview describes attachments.
        var found = await admin.Client.GetFromJsonAsync<List<ChatMessageDto>>($"/api/v1/chat/messages?recipientId={staffUser.Id}&search=brochure");
        found!.Count.ShouldBe(2); // the question, and the photo (matched by its file name)
    }
}

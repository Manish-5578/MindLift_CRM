using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MindLift.UI.Components;
using MindLift.UI.Localization;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

/// <summary>A person's profile photo shows where they appear; everyone else, and anyone without a photo, keeps initials.</summary>
public sealed class UserAvatarTests : BunitContext, IAsyncLifetime
{
    private static readonly Guid WithPhoto = Guid.NewGuid();
    private readonly List<string> _requests = [];

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public UserAvatarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddMindLiftLocalization();
        var http = new HttpClient(new Handler(this)) { BaseAddress = new Uri("http://localhost/") };
        Services.AddSingleton(new AvatarPhotos(http));
    }

    private sealed class Handler(UserAvatarTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner._requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith(WithPhoto.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0]);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public void A_user_with_a_photo_shows_it()
    {
        var cut = Render<UserAvatar>(p => p.Add(c => c.Name, "Asha Rao").Add(c => c.Id, WithPhoto).Add(c => c.IsUser, true));
        cut.WaitForAssertion(() => cut.Find("[data-testid=user-avatar-photo]").GetAttribute("src")!.ShouldStartWith("data:image/jpeg;base64,"));
    }

    [Fact]
    public void A_user_without_a_photo_keeps_initials()
    {
        var cut = Render<UserAvatar>(p => p.Add(c => c.Name, "Asha Rao").Add(c => c.Id, Guid.NewGuid()).Add(c => c.IsUser, true));
        cut.WaitForAssertion(() => _requests.Count.ShouldBe(1));
        cut.FindAll("[data-testid=user-avatar-photo]").ShouldBeEmpty();
        cut.Markup.ShouldContain("AR");
    }

    [Fact]
    public void Contacts_and_accounts_never_ask_for_a_photo()
    {
        var cut = Render<UserAvatar>(p => p.Add(c => c.Name, "Acme Ltd").Add(c => c.Id, WithPhoto));
        _requests.ShouldBeEmpty();
        cut.FindAll("[data-testid=user-avatar-photo]").ShouldBeEmpty();
    }

    [Fact]
    public void Many_avatars_for_one_person_make_one_request()
    {
        Render<UserAvatar>(p => p.Add(c => c.Name, "Asha Rao").Add(c => c.Id, WithPhoto).Add(c => c.IsUser, true));
        Render<UserAvatar>(p => p.Add(c => c.Name, "Asha Rao").Add(c => c.Id, WithPhoto).Add(c => c.IsUser, true));
        Render<UserAvatar>(p => p.Add(c => c.Name, "Asha Rao").Add(c => c.Id, WithPhoto).Add(c => c.IsUser, true));
        _requests.Count.ShouldBeLessThanOrEqualTo(1);
    }
}

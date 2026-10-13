using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MindLift.UI.Components;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

/// <summary>Plan 18.7 FileDropZone: type and size checks, the file list, and what the page is told.</summary>
public sealed class FileDropZoneTests : BunitContext, IAsyncLifetime
{
    // MudBlazor registers a service that can only be disposed asynchronously, so the test context must be too.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public FileDropZoneTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private (IRenderedComponent<FileDropZone> Zone, List<IReadOnlyList<IBrowserFile>> Calls) Create(Action<ComponentParameterCollectionBuilder<FileDropZone>>? configure = null)
    {
        var calls = new List<IReadOnlyList<IBrowserFile>>();
        var zone = Render<FileDropZone>(p =>
        {
            p.Add(c => c.FilesChanged, EventCallback.Factory.Create<IReadOnlyList<IBrowserFile>>(this, files => calls.Add(files)));
            configure?.Invoke(p);
        });
        return (zone, calls);
    }

    [Fact]
    public void It_shows_the_prompt_and_the_accepted_types_and_size()
    {
        var (zone, _) = Create();

        zone.Markup.ShouldContain("Drag &amp; drop files here");
        zone.Markup.ShouldContain("PDF, JPG, JPEG, PNG · up to 25 MB each");
    }

    [Fact]
    public void The_input_gets_the_accept_and_capture_settings()
    {
        var (zone, _) = Create(p => p.Add(c => c.Accept, "image/*,application/pdf").Add(c => c.Capture, "environment"));

        var input = zone.Find("input[type=file]");
        input.GetAttribute("accept").ShouldBe("image/*,application/pdf");
        input.GetAttribute("capture").ShouldBe("environment");
        input.HasAttribute("multiple").ShouldBeTrue();
    }

    [Fact]
    public void A_supported_file_is_listed_and_passed_to_the_page()
    {
        var (zone, calls) = Create();

        zone.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("%PDF-1.4", "contract.pdf"));

        zone.FindAll("[data-testid=file-item]").Count.ShouldBe(1);
        zone.Markup.ShouldContain("contract.pdf");
        calls.Last().Select(f => f.Name).ShouldBe(["contract.pdf"]);
    }

    [Fact]
    public void An_unsupported_type_shows_an_error_and_is_not_passed_on()
    {
        var (zone, calls) = Create();

        zone.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("MZ", "setup.exe"));

        zone.Find("[data-testid=file-error]").TextContent.ShouldBe("File type not supported");
        calls.Last().ShouldBeEmpty();
    }

    [Fact]
    public void A_file_over_the_limit_shows_the_limit_in_the_error()
    {
        var (zone, calls) = Create(p => p.Add(c => c.MaxSizeMb, 1));

        zone.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[2 * 1024 * 1024], "big.pdf"));

        zone.Find("[data-testid=file-error]").TextContent.ShouldBe("File is larger than 1 MB");
        calls.Last().ShouldBeEmpty();
    }

    [Fact]
    public void A_good_and_a_bad_file_together_pass_only_the_good_one()
    {
        var (zone, calls) = Create();

        zone.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("%PDF-1.4", "ok.pdf"),
            InputFileContent.CreateFromText("x", "notes.docx"));

        zone.FindAll("[data-testid=file-item]").Count.ShouldBe(2);
        calls.Last().Select(f => f.Name).ShouldBe(["ok.pdf"]);
    }

    [Fact]
    public void Removing_a_file_updates_the_list_and_the_page()
    {
        var (zone, calls) = Create();
        zone.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("%PDF-1.4", "a.pdf"));

        zone.Find("[data-testid=file-item] button").Click();

        zone.FindAll("[data-testid=file-item]").ShouldBeEmpty();
        calls.Last().ShouldBeEmpty();
    }

    [Fact]
    public void Dragging_over_the_zone_highlights_it_and_leaving_or_dropping_clears_the_highlight()
    {
        var (zone, _) = Create();
        var area = () => zone.Find("[data-testid=file-dropzone]");
        area().ClassList.ShouldNotContain("ml-dropzone--active");

        area().DragEnter();
        area().ClassList.ShouldContain("ml-dropzone--active");

        area().DragLeave();
        area().ClassList.ShouldNotContain("ml-dropzone--active");

        area().DragEnter();
        area().Drop();
        area().ClassList.ShouldNotContain("ml-dropzone--active");
    }

    [Fact]
    public void Extensions_are_matched_without_regard_to_case()
    {
        var (zone, calls) = Create();

        zone.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("%PDF-1.4", "SCAN.PDF"));

        calls.Last().Count.ShouldBe(1);
    }

    [Fact]
    public void Progress_for_a_file_name_shows_a_bar()
    {
        var (zone, _) = Create(p => p.Add(c => c.Progress, new Dictionary<string, double> { ["a.pdf"] = 40 }));

        zone.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("%PDF-1.4", "a.pdf"));

        zone.FindAll(".mud-progress-linear").Count.ShouldBe(1);
    }
}

using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class EmailTemplateTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, "+1-555-0199", "Acme Corporation", "new", "website", null, null, null, null, null);

    [Fact]
    public async Task Returns_404_when_email_template_builder_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var listResp = await admin.Client.GetAsync("/api/v1/email-templates");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/email-templates", new SaveEmailTemplateRequest
        {
            Name = "Disabled Template",
            Subject = "Test"
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var presetsResp = await admin.Client.GetAsync("/api/v1/email-templates/presets");
        presetsResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Presets_library_listing_and_instantiation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Fetch available presets
        var presetsResp = await admin.Client.GetAsync("/api/v1/email-templates/presets");
        presetsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var presets = await presetsResp.Content.ReadFromJsonAsync<List<EmailTemplatePresetDto>>();

        presets.ShouldNotBeNull();
        presets.Count.ShouldBeGreaterThanOrEqualTo(4);
        presets.ShouldContain(p => p.Key == "welcome-series");
        presets.ShouldContain(p => p.Key == "demo-followup");
        presets.ShouldContain(p => p.Key == "re-engagement");
        presets.ShouldContain(p => p.Key == "product-announcement");

        var welcomePreset = presets.First(p => p.Key == "welcome-series");
        welcomePreset.Blocks.ShouldNotBeEmpty();
        welcomePreset.MergeTags.ShouldContain("firstName");
        welcomePreset.MergeTags.ShouldContain("company");

        // 2. Instantiate template from preset
        var createResp = await admin.Client.PostAsJsonAsync(
            "/api/v1/email-templates/presets/welcome-series/create",
            new CloneEmailTemplateRequest { NewName = "Acme Custom Welcome" });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var template = await createResp.Content.ReadFromJsonAsync<EmailTemplateDto>();
        template.ShouldNotBeNull();
        template.Name.ShouldBe("Acme Custom Welcome");
        template.Category.ShouldBe("Onboarding");
        template.IsPreset.ShouldBeTrue();
        template.RenderedHtml.ShouldContain("<!DOCTYPE html>");
        template.RenderedHtml.ShouldContain("Welcome aboard, {{firstName}}!");
        template.PlainTextBody.ShouldContain("MindLift CRM provides you with intelligent lead tracking");
        template.MergeTags.ShouldContain("firstName");
        template.MergeTags.ShouldContain("company");
    }

    [Fact]
    public async Task Custom_block_based_template_crud_and_compilation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Build a custom visual block template
        var blocks = new List<EmailBlockDto>
        {
            new()
            {
                Type = "header",
                Properties = new() { ["logoUrl"] = "https://acme.org/brand.png", ["align"] = "center" }
            },
            new()
            {
                Type = "heading",
                Properties = new() { ["text"] = "Hello {{firstName}} from {{company}}", ["level"] = 1 }
            },
            new()
            {
                Type = "text",
                Properties = new() { ["content"] = "We noticed your recent activity on our platform.\n\nPlease check your account status below." }
            },
            new()
            {
                Type = "button",
                Properties = new() { ["label"] = "Activate Account", ["url"] = "https://acme.org/activate?account={{company}}" }
            },
            new()
            {
                Type = "divider",
                Properties = new() { ["color"] = "#cccccc" }
            },
            new()
            {
                Type = "footer",
                Properties = new() { ["companyName"] = "Acme Global", ["showUnsubscribe"] = true }
            }
        };

        var saveReq = new SaveEmailTemplateRequest
        {
            Name = "Account Activation Sequence",
            Subject = "Important: Activate your account for {{company}}",
            PreheaderText = "Action required to access your dashboard",
            Category = "Transactional",
            Description = "Onboarding activation email with deep links",
            Blocks = blocks
        };

        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/email-templates", saveReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await postResp.Content.ReadFromJsonAsync<EmailTemplateDto>();

        created.ShouldNotBeNull();
        created.Name.ShouldBe("Account Activation Sequence");
        created.Category.ShouldBe("Transactional");
        created.Blocks.Count.ShouldBe(6);

        // Verify HTML compilation
        created.RenderedHtml.ShouldContain("<!DOCTYPE html>");
        created.RenderedHtml.ShouldContain("Hello {{firstName}} from {{company}}");
        created.RenderedHtml.ShouldContain("Activate Account");
        created.RenderedHtml.ShouldContain("{{unsubscribeUrl}}");

        // Verify PlainText compilation
        created.PlainTextBody.ShouldContain("HELLO {{FIRSTNAME}} FROM {{COMPANY}}");
        created.PlainTextBody.ShouldContain("[ Activate Account: https://acme.org/activate?account={{company}} ]");
        created.PlainTextBody.ShouldContain("Unsubscribe: {{unsubscribeUrl}}");

        // Verify automatic merge tag extraction
        created.MergeTags.ShouldContain("company");
        created.MergeTags.ShouldContain("firstName");
        created.MergeTags.ShouldContain("unsubscribeUrl");

        // 2. Fetch by ID
        var getResp = await admin.Client.GetAsync($"/api/v1/email-templates/{created.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<EmailTemplateDto>();
        fetched.ShouldNotBeNull();
        fetched.Id.ShouldBe(created.Id);
        fetched.Blocks.Count.ShouldBe(6);

        // 3. Update template
        var updateReq = new SaveEmailTemplateRequest
        {
            Name = "Account Activation Sequence - v2",
            Subject = "Updated: Activate your account for {{company}}",
            Category = "Onboarding",
            Blocks = blocks,
            IsActive = true
        };
        var putResp = await admin.Client.PutAsJsonAsync($"/api/v1/email-templates/{created.Id}", updateReq);
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<EmailTemplateDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Account Activation Sequence - v2");
        updated.Category.ShouldBe("Onboarding");

        // 4. List templates with filter
        var listResp = await admin.Client.GetAsync("/api/v1/email-templates?category=Onboarding");
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summaries = await listResp.Content.ReadFromJsonAsync<List<EmailTemplateSummaryDto>>();
        summaries.ShouldNotBeNull();
        summaries.ShouldContain(s => s.Id == created.Id);
    }

    [Fact]
    public async Task Ad_hoc_preview_rendering_with_variable_substitution()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var previewReq = new RenderTemplateRequest
        {
            Subject = "Hi {{firstName}}, great connecting with {{company}}!",
            HtmlContent = "<p>Hello {{firstName}},</p><p>We are ready to deploy at {{company}}.</p><a href=\"{{unsubscribeUrl}}\">Opt out</a>",
            PlainTextContent = "Hello {{firstName}},\nWe are ready at {{company}}.\nOpt out: {{unsubscribeUrl}}",
            Variables = new Dictionary<string, string>
            {
                ["firstName"] = "Jane",
                ["company"] = "Stark Enterprises"
            }
        };

        var previewResp = await admin.Client.PostAsJsonAsync("/api/v1/email-templates/render-preview", previewReq);
        previewResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rendered = await previewResp.Content.ReadFromJsonAsync<RenderedEmailResult>();

        rendered.ShouldNotBeNull();
        rendered.Subject.ShouldBe("Hi Jane, great connecting with Stark Enterprises!");
        rendered.HtmlBody.ShouldContain("Hello Jane,");
        rendered.HtmlBody.ShouldContain("We are ready to deploy at Stark Enterprises.");
        rendered.PlainTextBody.ShouldContain("Hello Jane,\nWe are ready at Stark Enterprises.");
        rendered.DetectedMergeTags.ShouldContain("firstName");
        rendered.DetectedMergeTags.ShouldContain("company");
        rendered.DetectedMergeTags.ShouldContain("unsubscribeUrl");
        rendered.MissingVariables.ShouldBeEmpty();
    }

    [Fact]
    public async Task Live_rendering_against_crm_lead_record()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Alexander", "Hamilton", "alex@treasury.gov"));
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 2. Create a template using lead merge tags
        var saveTemplate = new SaveEmailTemplateRequest
        {
            Name = "Lead Meeting Intro",
            Subject = "Welcome {{firstName}}, next steps for {{company}}",
            CustomHtml = "<p>Dear {{firstName}} {{lastName}},</p><p>We received your inquiry from {{leadSource}} regarding {{company}}.</p>",
            CustomPlainText = "Dear {{firstName}} {{lastName}},\nWe received your inquiry regarding {{company}}."
        };
        var templateResp = await admin.Client.PostAsJsonAsync("/api/v1/email-templates", saveTemplate);
        templateResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var template = await templateResp.Content.ReadFromJsonAsync<EmailTemplateDto>();
        template.ShouldNotBeNull();

        // 3. Render template bound to lead
        var renderReq = new RenderWithEntityRequest
        {
            EntityType = "lead",
            EntityId = lead.Id
        };
        var renderResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-templates/{template.Id}/render", renderReq);
        renderResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await renderResp.Content.ReadFromJsonAsync<RenderedEmailResult>();

        result.ShouldNotBeNull();
        result.Subject.ShouldBe("Welcome Alexander, next steps for Acme Corporation");
        result.HtmlBody.ShouldContain("Dear Alexander Hamilton,");
        result.HtmlBody.ShouldContain("regarding Acme Corporation.");
        result.PlainTextBody.ShouldContain("Dear Alexander Hamilton,");
    }

    [Fact]
    public async Task Clone_and_delete_template_lifecycle()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create original template
        var origResp = await admin.Client.PostAsJsonAsync("/api/v1/email-templates", new SaveEmailTemplateRequest
        {
            Name = "Original Master Template",
            Subject = "Master Subject",
            CustomHtml = "<h1>Original Content</h1>"
        });
        origResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var orig = await origResp.Content.ReadFromJsonAsync<EmailTemplateDto>();
        orig.ShouldNotBeNull();

        // 2. Clone template
        var cloneResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-templates/{orig.Id}/clone", new CloneEmailTemplateRequest
        {
            NewName = "Variant B - Cloned Template"
        });
        cloneResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clone = await cloneResp.Content.ReadFromJsonAsync<EmailTemplateDto>();

        clone.ShouldNotBeNull();
        clone.Id.ShouldNotBe(orig.Id);
        clone.Name.ShouldBe("Variant B - Cloned Template");
        clone.Subject.ShouldBe(orig.Subject);
        clone.RenderedHtml.ShouldBe(orig.RenderedHtml);

        // 3. Delete original template
        var delResp = await admin.Client.DeleteAsync($"/api/v1/email-templates/{orig.Id}");
        delResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify original is now 404 (soft deleted)
        var getDeletedResp = await admin.Client.GetAsync($"/api/v1/email-templates/{orig.Id}");
        getDeletedResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Verify clone remains active
        var getCloneResp = await admin.Client.GetAsync($"/api/v1/email-templates/{clone.Id}");
        getCloneResp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Strict_multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/email-template-builder", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates a confidential template
        var createResp = await tenantA.Client.PostAsJsonAsync("/api/v1/email-templates", new SaveEmailTemplateRequest
        {
            Name = "Tenant A Confidential Layout",
            Subject = "Secret Internal Communication",
            CustomHtml = "<p>Confidential to Tenant A</p>"
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var templateA = await createResp.Content.ReadFromJsonAsync<EmailTemplateDto>();
        templateA.ShouldNotBeNull();

        // Tenant B attempts to read Tenant A's template -> 404
        var getCrossResp = await tenantB.Client.GetAsync($"/api/v1/email-templates/{templateA.Id}");
        getCrossResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to update Tenant A's template -> 404
        var updateCrossResp = await tenantB.Client.PutAsJsonAsync($"/api/v1/email-templates/{templateA.Id}", new SaveEmailTemplateRequest
        {
            Name = "Hacked Name"
        });
        updateCrossResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to clone Tenant A's template -> 404
        var cloneCrossResp = await tenantB.Client.PostAsJsonAsync($"/api/v1/email-templates/{templateA.Id}/clone", new CloneEmailTemplateRequest());
        cloneCrossResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to render Tenant A's template -> 404
        var renderCrossResp = await tenantB.Client.PostAsJsonAsync($"/api/v1/email-templates/{templateA.Id}/render", new RenderWithEntityRequest
        {
            EntityType = "lead",
            EntityId = Guid.NewGuid()
        });
        renderCrossResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to delete Tenant A's template -> 404
        var deleteCrossResp = await tenantB.Client.DeleteAsync($"/api/v1/email-templates/{templateA.Id}");
        deleteCrossResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B's template list does not contain Tenant A's template
        var listBResp = await tenantB.Client.GetAsync("/api/v1/email-templates");
        var listB = await listBResp.Content.ReadFromJsonAsync<List<EmailTemplateSummaryDto>>();
        listB.ShouldNotBeNull();
        listB.ShouldNotContain(t => t.Id == templateA.Id);
    }
}

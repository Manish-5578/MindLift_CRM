using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Settings;

/// <summary>Plan 13 and M3 DoD: custom fields, pipelines, picklists, currencies, modules and layouts, through the real API and database.</summary>
public sealed class SettingsTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static readonly FilterContext Ctx = new(
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai"), DateTime.UtcNow, DayOfWeek.Monday, 1, null);

    [Fact]
    public async Task A_new_custom_field_appears_in_the_bootstrap_and_filters_records_on_its_value()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");

        var create = await owner.Client.PostAsJsonAsync("/api/v1/settings/custom-fields", new SaveCustomFieldRequest(
            "leads", "cf_budget", "Budget", "Ø§Ù„Ù…ÙŠØ²Ø§Ù†ÙŠØ©", FieldType.Currency, IsRequired: false, ShowInList: true,
            Filterable: true, Section: "Requirements", SortOrder: 1, Options: null));
        create.StatusCode.ShouldBe(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());

        var bootstrap = await owner.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap");
        bootstrap!.Fields["leads"].Select(f => f.Key).ShouldContain("cf_budget");

        await using var tdb = await DbAsync(me!);
        var db = tdb.Db;
        db.Leads.AddRange(
            new Lead { FirstName = "Big", OwnerId = me!.UserId, CustomFields = JsonDocument.Parse("{\"cf_budget\": 2500000}") },
            new Lead { FirstName = "Small", OwnerId = me.UserId, CustomFields = JsonDocument.Parse("{\"cf_budget\": 90000}") },
            new Lead { FirstName = "Unset", OwnerId = me.UserId });
        await db.SaveChangesAsync();

        var fields = await DefinitionsAsync(me!, "leads");
        var group = new FilterGroup { Conditions = [new FilterCondition { Field = "cf_budget", Op = FilterOperator.Gt, Value = JsonSerializer.SerializeToElement(1000000) }] };
        var expression = FilterCompiler.Compile<Lead>(group, fields, Ctx);

        var matches = await db.Leads.Where(expression).Select(l => l.FirstName).ToListAsync();
        matches.ShouldBe(["Big"]);
    }

    [Fact]
    public async Task Deleting_a_custom_field_with_values_needs_explicit_purge()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        await owner.Client.PostAsJsonAsync("/api/v1/settings/custom-fields", new SaveCustomFieldRequest(
            "leads", "cf_note", "Note", null, FieldType.Text, false, false, true, null, 0, null));

        await using (var tdb = await DbAsync(me!))
        {
            tdb.Db.Leads.Add(new Lead { FirstName = "Has note", OwnerId = me!.UserId, CustomFields = JsonDocument.Parse("{\"cf_note\": \"call back\"}") });
            await tdb.Db.SaveChangesAsync();
        }

        var fields = await owner.Client.GetFromJsonAsync<List<CustomFieldDto>>("/api/v1/settings/custom-fields/leads");
        var id = fields!.Single(f => f.Key == "cf_note").Id;

        var refused = await owner.Client.DeleteAsync($"/api/v1/settings/custom-fields/{id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("field_has_data");

        var purged = await owner.Client.DeleteAsync($"/api/v1/settings/custom-fields/{id}?purgeData=true");
        purged.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var checkDb = await DbAsync(me!);
        var check = checkDb.Db;
        var lead = await checkDb.Db.Leads.SingleAsync(l => l.FirstName == "Has note");
        lead.CustomFields!.RootElement.TryGetProperty("cf_note", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Stage_with_deals_cannot_be_deleted_until_the_deals_are_moved()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var pipeline = await CreatePipelineAsync(owner, "Sales");
        var stage = pipeline.Stages.First(s => s.StageType == "Open");
        var target = pipeline.Stages.First(s => s.StageType == "Won");

        await using (var tdb = await DbAsync(me!))
        {
            tdb.Db.Deals.Add(new Deal { Name = "Villa", PipelineId = pipeline.Id, StageId = stage.Id, OwnerId = me!.UserId, Amount = 100 });
            await tdb.Db.SaveChangesAsync();
        }

        var refused = await owner.Client.DeleteAsync($"/api/v1/settings/pipelines/{pipeline.Id}/stages/{stage.Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("stage_has_deals");

        var moved = await owner.Client.DeleteAsync($"/api/v1/settings/pipelines/{pipeline.Id}/stages/{stage.Id}?moveDealsToStageId={target.Id}");
        moved.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // Removing the only open stage breaks the pipeline rules.
        (await Workspaces.ErrorCodeAsync(moved)).ShouldBe("pipeline_rules");
    }

    [Fact]
    public async Task Changing_the_won_stage_to_open_is_refused_and_nothing_is_saved()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var pipeline = await CreatePipelineAsync(owner, "Renewals");
        var won = pipeline.Stages.Single(s => s.StageType == "Won");

        var response = await owner.Client.PutAsJsonAsync(
            $"/api/v1/settings/pipelines/{pipeline.Id}/stages/{won.Id}",
            new SaveStageRequest("Won", null, 100, "Open", null, null, won.SortOrder));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var after = await owner.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        after!.Single(p => p.Id == pipeline.Id).Stages.Single(s => s.Id == won.Id).StageType.ShouldBe("Won");
    }

    [Fact]
    public async Task A_picklist_value_in_use_needs_a_replacement_and_the_records_are_moved()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var added = await owner.Client.PostAsJsonAsync("/api/v1/settings/picklists/lead_source/values",
            new SavePicklistValueRequest("trade_show", "Trade show", null, null, null, 50, false, true));
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var value = await added.Content.ReadFromJsonAsync<PicklistValueDto>();

        await using (var tdb = await DbAsync(me!))
        {
            tdb.Db.Leads.Add(new Lead { FirstName = "Expo", OwnerId = me!.UserId, Source = "trade_show" });
            await tdb.Db.SaveChangesAsync();
        }

        var refused = await owner.Client.DeleteAsync($"/api/v1/settings/picklists/lead_source/values/{value!.Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("picklist_value_in_use");

        var replaced = await owner.Client.DeleteAsync($"/api/v1/settings/picklists/lead_source/values/{value.Id}?replaceWith=other");
        replaced.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var checkDb = await DbAsync(me!);
        var check = checkDb.Db;
        (await checkDb.Db.Leads.SingleAsync(l => l.FirstName == "Expo")).Source.ShouldBe("other");
    }

    [Fact]
    public async Task Staff_cannot_change_settings_but_admins_can()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "settings");

        (await staff.Client.GetAsync("/api/v1/settings/company")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.Client.GetAsync("/api/v1/settings/company")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Exchange_rates_need_the_currency_to_be_enabled_first()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var rate = new SaveExchangeRateRequest("EUR", 4.1m, new DateTime(2026, 10, 1));

        var refused = await owner.Client.PostAsJsonAsync("/api/v1/settings/exchange-rates", rate);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("currency_not_enabled");

        var enabled = await owner.Client.PutAsJsonAsync("/api/v1/settings/currencies", new SaveCurrencySettingsRequest("AED", ["EUR"]));
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK);

        var ok = await owner.Client.PostAsJsonAsync("/api/v1/settings/exchange-rates", rate);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Layouts_only_accept_fields_that_exist()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var response = await owner.Client.PutAsJsonAsync("/api/v1/settings/layouts/leads/Form",
            new SaveLayoutRequest([new LayoutSectionDto("Contact", null, 2, ["first_name", "no_such_field"])]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Every_settings_change_moves_the_bootstrap_version_and_disabling_a_module_is_visible()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var before = await owner.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap");

        var toggled = await owner.Client.PutAsJsonAsync("/api/v1/settings/modules/Calendar",
            new SaveModuleSettingRequest(false, "Calendar", "Calendar", 7));
        toggled.StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await owner.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap");
        after!.Version.ShouldNotBe(before!.Version);
        after.Modules.Single(m => m.Module == "Calendar").IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Saved_views_are_visible_to_their_creator()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var created = await owner.Client.PostAsJsonAsync("/api/v1/views", new SaveSavedViewRequest(
            "leads", "My open leads", IsShared: false, IsDefault: true,
            Filter: new FilterGroup { Conditions = [new FilterCondition { Field = "owner_id", Op = FilterOperator.Eq, Value = JsonSerializer.SerializeToElement("@me") }] },
            Columns: ["first_name", "status"], SortField: "created_at", SortDir: "desc"));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        var list = await owner.Client.GetFromJsonAsync<List<SavedViewDto>>("/api/v1/views/leads");
        list!.Select(v => v.Name).ShouldContain("My open leads");
    }

    private async Task<PipelineDto> CreatePipelineAsync(Session owner, string name)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest(name, null, false, true, 10));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PipelineDto>())!;
    }

    /// <summary>A database context scoped to the caller's tenant, as the API would see it. Dispose it to end the scope.</summary>
    private async Task<TenantDb> DbAsync(MeResponse me)
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me.TenantId, me.UserId);
        return await Task.FromResult(new TenantDb(scope));
    }

    private async Task<IReadOnlyList<FieldDef>> DefinitionsAsync(MeResponse me, string entity)
    {
        await using var tdb = await DbAsync(me);
        var fields = tdb.Scope.ServiceProvider.GetRequiredService<FieldSettingsService>();
        return await fields.RegistryAsync(entity);
    }

    private sealed class TenantDb(AsyncServiceScope scope) : IAsyncDisposable
    {
        public AsyncServiceScope Scope { get; } = scope;
        public AppDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }}


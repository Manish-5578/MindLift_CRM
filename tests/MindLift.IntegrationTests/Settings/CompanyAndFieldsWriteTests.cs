using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Settings;

/// <summary>Write paths of the settings screens. Bad input is a field error (400 or 409), never a server error, and one workspace never reaches another's settings.</summary>
public sealed class CompanyAndFieldsWriteTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<SaveCompanySettingsRequest> CurrentCompanyAsync(Session admin)
    {
        var c = (await admin.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company"))!;
        return new SaveCompanySettingsRequest(c.CompanyName, c.Email, c.Phone, c.DefaultLanguage, c.TimeZoneId, c.WeekStartDay,
            c.FiscalYearStartMonth, c.TaxEnabled, c.TaxLabel, c.DefaultTaxRate, c.TaxRegistrationNumber, c.PrimaryColor);
    }

    private static Task<HttpResponseMessage> SaveCompanyAsync(Session admin, SaveCompanySettingsRequest request)
        => admin.Client.PutAsJsonAsync("/api/v1/settings/company", request);

    private static async Task<HashSet<string>> FieldsInErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToHashSet()
            : [];
    }

    // ---------- company ----------

    [Fact]
    public async Task Company_details_can_be_saved_and_read_back()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var current = await CurrentCompanyAsync(admin);

        var response = await SaveCompanyAsync(admin, current with { CompanyName = "Gulf Trading LLC", Phone = "+971 4 555 0100", TimeZoneId = "Asia/Dubai", WeekStartDay = 0, FiscalYearStartMonth = 4, TaxEnabled = true, TaxLabel = "VAT", DefaultTaxRate = 5m });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await admin.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company"))!;
        saved.CompanyName.ShouldBe("Gulf Trading LLC");
        saved.WeekStartDay.ShouldBe(0);
        saved.FiscalYearStartMonth.ShouldBe(4);
        saved.DefaultTaxRate.ShouldBe(5m);
    }

    [Theory]
    [InlineData("timezone", "timezoneid")]
    [InlineData("language", "defaultlanguage")]
    [InlineData("taxhigh", "defaulttaxrate")]
    [InlineData("taxlow", "defaulttaxrate")]
    [InlineData("weekday", "weekstartday")]
    [InlineData("monthhigh", "fiscalyearstartmonth")]
    [InlineData("monthlow", "fiscalyearstartmonth")]
    [InlineData("name", "companyname")]
    public async Task Invalid_company_values_are_field_errors(string which, string expectedField)
    {
        var admin = await Workspaces.SignupAsync(factory);
        var current = await CurrentCompanyAsync(admin);
        var bad = which switch
        {
            "timezone" => current with { TimeZoneId = "Mars/Olympus_Mons" },
            "language" => current with { DefaultLanguage = "fr" },
            "taxhigh" => current with { DefaultTaxRate = 100.5m },
            "taxlow" => current with { DefaultTaxRate = -1m },
            "weekday" => current with { WeekStartDay = 7 },
            "monthhigh" => current with { FiscalYearStartMonth = 13 },
            "monthlow" => current with { FiscalYearStartMonth = 0 },
            _ => current with { CompanyName = "   " },
        };

        var response = await SaveCompanyAsync(admin, bad);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await FieldsInErrorAsync(response)).ShouldContain(expectedField);
    }

    [Fact]
    public async Task A_company_value_longer_than_its_limit_is_refused_not_a_server_error()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var current = await CurrentCompanyAsync(admin);

        var response = await SaveCompanyAsync(admin, current with { CompanyName = new string('c', 201) });

        ((int)response.StatusCode).ShouldBeInRange(400, 499, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#FFFF00")]
    [InlineData("#FFFFFF")]
    public async Task A_brand_colour_that_is_not_a_readable_hex_colour_is_refused(string colour)
    {
        var admin = await Workspaces.SignupAsync(factory);
        var current = await CurrentCompanyAsync(admin);

        var response = await SaveCompanyAsync(admin, current with { PrimaryColor = colour });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FieldsInErrorAsync(response)).ShouldContain("primarycolor");
        (await admin.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company"))!.PrimaryColor.ShouldBeNull();
    }

    [Fact]
    public async Task A_valid_brand_colour_is_saved_in_capitals_and_reaches_the_app_and_can_be_cleared()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var current = await CurrentCompanyAsync(admin);

        (await SaveCompanyAsync(admin, current with { PrimaryColor = "#0d9488" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var bootstrap = (await admin.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap"))!;
        bootstrap.Company.PrimaryColor.ShouldBe("#0D9488");

        (await SaveCompanyAsync(admin, current with { PrimaryColor = "  " })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company"))!.PrimaryColor.ShouldBeNull();
    }

    [Fact]
    public async Task One_workspaces_company_settings_never_change_anothers()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        var before = (await b.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company"))!.CompanyName;

        await SaveCompanyAsync(a, (await CurrentCompanyAsync(a)) with { CompanyName = "Only A" });

        (await b.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company"))!.CompanyName.ShouldBe(before);
    }

    // ---------- modules ----------

    [Fact]
    public async Task A_module_can_be_renamed_and_the_new_names_reach_the_app()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/modules/Leads", new SaveModuleSettingRequest(true, "Enquiry", "Enquiries", 2));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var bootstrap = (await admin.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap"))!;
        var leads = bootstrap.Modules.Single(m => m.Module == "Leads");
        leads.SingularLabel.ShouldBe("Enquiry");
        leads.PluralLabel.ShouldBe("Enquiries");
    }

    [Fact]
    public async Task A_module_label_longer_than_its_limit_is_refused_not_a_server_error()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/modules/Leads", new SaveModuleSettingRequest(true, new string('s', 101), "Leads", 2));

        ((int)response.StatusCode).ShouldBeInRange(400, 499, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_module_with_no_label_is_refused()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/modules/Leads", new SaveModuleSettingRequest(true, "", "", 2));

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task An_unknown_module_is_not_found()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/modules/NotAModule", new SaveModuleSettingRequest(true, "X", "Xs", 1));

        ((int)response.StatusCode).ShouldBeInRange(400, 404);
    }

    [Fact]
    public async Task The_settings_module_cannot_be_switched_off_because_that_would_lock_the_admin_out()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/modules/Settings", new SaveModuleSettingRequest(false, "Setting", "Settings", 99));

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
        (await admin.Client.GetAsync("/api/v1/settings/company")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- custom fields ----------

    private static Task<HttpResponseMessage> AddFieldAsync(Session admin, string key, string label = "Budget", MindLift.Shared.Filtering.FieldType type = MindLift.Shared.Filtering.FieldType.Currency,
        bool required = false, IReadOnlyList<PicklistOptionDto>? options = null, string entity = "leads")
        => admin.Client.PostAsJsonAsync("/api/v1/settings/custom-fields", new SaveCustomFieldRequest(entity, key, label, null, type, required, true, true, null, 0, options));

    [Theory]
    [InlineData("budget")]
    [InlineData("cf_")]
    [InlineData("cf_Budget")]
    [InlineData("cf_has space")]
    [InlineData("cf_has-dash")]
    [InlineData("cf_budget; drop table leads")]
    public async Task A_custom_field_key_must_start_with_cf_and_use_lowercase_letters_digits_and_underscores(string key)
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await AddFieldAsync(admin, key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_custom_field_key_and_label_have_length_limits()
    {
        var admin = await Workspaces.SignupAsync(factory);

        ((int)(await AddFieldAsync(admin, "cf_" + new string('k', 60))).StatusCode).ShouldBeInRange(400, 499);
        ((int)(await AddFieldAsync(admin, "cf_long_label", label: new string('l', 300))).StatusCode).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task A_custom_field_key_is_unique_per_record_type_but_can_repeat_on_another_type()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await AddFieldAsync(admin, "cf_budget")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var same = await AddFieldAsync(admin, "cf_budget");
        var other = await AddFieldAsync(admin, "cf_budget", entity: "contacts");

        same.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_choice_field_needs_options_and_they_must_be_distinct()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var none = await AddFieldAsync(admin, "cf_size", type: MindLift.Shared.Filtering.FieldType.Picklist);
        var dupes = await AddFieldAsync(admin, "cf_size", type: MindLift.Shared.Filtering.FieldType.Picklist,
            options: [new("s", "Small", null, null), new("s", "Also small", null, null)]);
        var fine = await AddFieldAsync(admin, "cf_size", type: MindLift.Shared.Filtering.FieldType.Picklist,
            options: [new("s", "Small", null, null), new("l", "Large", null, null)]);

        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        dupes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fine.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_field_for_an_unknown_record_type_is_refused()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await AddFieldAsync(admin, "cf_x", entity: "spaceships");

        ((int)response.StatusCode).ShouldBeInRange(400, 404);
    }

    [Fact]
    public async Task A_required_custom_field_must_be_given_when_a_lead_is_created()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await AddFieldAsync(admin, "cf_budget", required: true)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var without = await admin.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("No", "Budget", null, null, null, "new", "website", null, null, null, null, null));
        var with = await admin.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Has", "Budget", null, null, null, "new", "website", null, null, null, null,
            new Dictionary<string, JsonElement> { ["cf_budget"] = JsonSerializer.SerializeToElement(1500) }));

        without.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FieldsInErrorAsync(without)).ShouldContain(f => f.Contains("cf_budget"));
        with.StatusCode.ShouldBe(HttpStatusCode.OK, await with.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_custom_value_of_the_wrong_kind_is_refused()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await AddFieldAsync(admin, "cf_budget")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await admin.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Bad", "Value", null, null, null, "new", "website", null, null, null, null,
            new Dictionary<string, JsonElement> { ["cf_budget"] = JsonSerializer.SerializeToElement("a lot") }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_workspaces_custom_fields_are_invisible_to_another()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        await AddFieldAsync(a, "cf_secret");

        (await b.Client.GetFromJsonAsync<List<CustomFieldDto>>("/api/v1/settings/custom-fields/leads"))!.ShouldBeEmpty();
        var bootstrap = (await b.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap"))!;
        bootstrap.Fields["leads"].ShouldNotContain(f => f.Key == "cf_secret");
    }

    // ---------- picklists ----------

    [Fact]
    public async Task A_picklist_code_cannot_be_added_twice_or_changed_later()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var add = await admin.Client.PostAsJsonAsync("/api/v1/settings/picklists/lead_source/values", new SavePicklistValueRequest("trade_show", "Trade show", null, null, null, 100, false, true));
        add.StatusCode.ShouldBe(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        var value = (await add.Content.ReadFromJsonAsync<PicklistValueDto>())!;

        var again = await admin.Client.PostAsJsonAsync("/api/v1/settings/picklists/lead_source/values", new SavePicklistValueRequest("trade_show", "Duplicate", null, null, null, 101, false, true));
        var rename = await admin.Client.PutAsJsonAsync($"/api/v1/settings/picklists/lead_source/values/{value.Id}", new SavePicklistValueRequest("expo", "Trade show", null, null, null, 100, false, true));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(again)).ShouldBe("picklist_value_taken");
        rename.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(rename)).ShouldBe("picklist_value_fixed");
    }

    [Fact]
    public async Task A_built_in_picklist_value_cannot_be_deleted_but_a_custom_one_can()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var lists = (await admin.Client.GetFromJsonAsync<List<PicklistDto>>("/api/v1/settings/picklists"))!;
        var builtIn = lists.Single(l => l.Key == "lead_source").Values.First(v => v.IsSystem);
        var custom = await (await admin.Client.PostAsJsonAsync("/api/v1/settings/picklists/lead_source/values",
            new SavePicklistValueRequest("temp_source", "Temp", null, null, null, 100, false, true))).Content.ReadFromJsonAsync<PicklistValueDto>();

        var refused = await admin.Client.DeleteAsync($"/api/v1/settings/picklists/lead_source/values/{builtIn.Id}");
        var removed = await admin.Client.DeleteAsync($"/api/v1/settings/picklists/lead_source/values/{custom!.Id}");

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("system_value");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_picklist_label_longer_than_its_limit_is_refused_not_a_server_error()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/picklists/lead_source/values",
            new SavePicklistValueRequest("long_one", new string('l', 121), null, null, null, 100, false, true));

        ((int)response.StatusCode).ShouldBeInRange(400, 499, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_value_cannot_be_added_to_a_picklist_that_does_not_exist()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/picklists/not_a_list/values", new SavePicklistValueRequest("x", "X", null, null, null, 1, false, true));

        ((int)response.StatusCode).ShouldBeInRange(400, 404);
    }

    // ---------- roles ----------

    [Fact]
    public async Task A_role_can_be_created_cloned_renamed_and_deleted_and_names_are_unique()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var created = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Sales lead", "Runs the sales team", MindLift.Shared.Enums.DashboardView.Manager));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var role = (await created.Content.ReadFromJsonAsync<RoleDto>())!;

        var duplicate = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Sales lead", null, MindLift.Shared.Enums.DashboardView.Staff));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(duplicate)).ShouldBe("role_name_taken");

        var clone = await admin.Client.PostAsJsonAsync($"/api/v1/roles/{role.Id}/clone", new SaveRoleRequest("Sales lead copy", null, MindLift.Shared.Enums.DashboardView.Manager));
        clone.StatusCode.ShouldBe(HttpStatusCode.OK, await clone.Content.ReadAsStringAsync());
        var copy = (await clone.Content.ReadFromJsonAsync<RoleDto>())!;

        var original = (await admin.Client.GetFromJsonAsync<List<RolePermissionDto>>($"/api/v1/roles/{role.Id}/permissions"))!;
        var copied = (await admin.Client.GetFromJsonAsync<List<RolePermissionDto>>($"/api/v1/roles/{copy.Id}/permissions"))!;
        copied.ShouldBe(original);

        (await admin.Client.DeleteAsync($"/api/v1/roles/{copy.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles"))!.ShouldNotContain(r => r.Id == copy.Id);
    }

    [Fact]
    public async Task A_role_name_that_is_too_long_or_empty_is_refused()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest(new string('r', 101), null, MindLift.Shared.Enums.DashboardView.Staff))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("", null, MindLift.Shared.Enums.DashboardView.Staff))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

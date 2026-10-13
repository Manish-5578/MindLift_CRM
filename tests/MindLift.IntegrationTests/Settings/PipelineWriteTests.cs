using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Settings;

/// <summary>Plan 7.4 pipelines: a pipeline always has open stages, then exactly one Won and one Lost at the end.</summary>
public sealed class PipelineWriteTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    /// <summary>A workspace that has chosen the general industry, which is what creates the first pipeline.</summary>
    private async Task<Session> NewWorkspaceAsync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var response = await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return admin;
    }

    private static async Task<PipelineDto> CreateAsync(Session admin, string name, bool isDefault = false)
    {
        var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest(name, null, isDefault, true, 5));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PipelineDto>())!;
    }

    private static async Task<List<PipelineDto>> ListAsync(Session admin)
        => (await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;

    private static Task<HttpResponseMessage> AddStageAsync(Session admin, Guid pipelineId, string name, string type, int probability, int order)
        => admin.Client.PostAsJsonAsync($"/api/v1/settings/pipelines/{pipelineId}/stages", new SaveStageRequest(name, null, probability, type, null, null, order));

    [Fact]
    public async Task A_new_pipeline_starts_with_a_valid_set_of_stages()
    {
        var admin = await NewWorkspaceAsync();

        var pipeline = await CreateAsync(admin, "Renewals");

        var types = pipeline.Stages.OrderBy(s => s.SortOrder).Select(s => s.StageType).ToList();
        types.ShouldContain("Open");
        types.Count(t => t == "Won").ShouldBe(1);
        types.Count(t => t == "Lost").ShouldBe(1);
        types.Take(types.Count - 2).ShouldAllBe(t => t == "Open");
        pipeline.Stages.Single(s => s.StageType == "Won").Probability.ShouldBe(100);
        pipeline.Stages.Single(s => s.StageType == "Lost").Probability.ShouldBe(0);
    }

    [Fact]
    public async Task A_pipeline_name_must_be_unique()
    {
        var admin = await NewWorkspaceAsync();
        await CreateAsync(admin, "Renewals");

        var again = await admin.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Renewals", null, false, true, 6));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(again)).ShouldBe("pipeline_name_taken");
    }

    [Fact]
    public async Task Making_a_pipeline_the_default_takes_the_default_from_the_old_one()
    {
        var admin = await NewWorkspaceAsync();
        var created = await CreateAsync(admin, "Renewals", isDefault: true);

        var all = await ListAsync(admin);

        all.Count(p => p.IsDefault).ShouldBe(1);
        all.Single(p => p.IsDefault).Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task A_pipeline_can_be_renamed_and_switched_off()
    {
        var admin = await NewWorkspaceAsync();
        var created = await CreateAsync(admin, "Renewals");

        var response = await admin.Client.PutAsJsonAsync($"/api/v1/settings/pipelines/{created.Id}", new SavePipelineRequest("Renewals 2027", "تجديدات", false, false, 5));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await ListAsync(admin)).Single(p => p.Id == created.Id);
        saved.Name.ShouldBe("Renewals 2027");
        saved.NameAr.ShouldBe("تجديدات");
        saved.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task The_last_pipeline_cannot_be_deleted()
    {
        var admin = await NewWorkspaceAsync();
        var only = (await ListAsync(admin)).Single();

        var response = await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{only.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("last_pipeline");
    }

    [Fact]
    public async Task An_empty_pipeline_can_be_deleted_when_another_remains()
    {
        var admin = await NewWorkspaceAsync();
        var extra = await CreateAsync(admin, "Temporary");

        (await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{extra.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(admin)).ShouldNotContain(p => p.Id == extra.Id);
    }

    [Fact]
    public async Task A_pipeline_with_deals_needs_a_stage_to_move_them_to()
    {
        var admin = await NewWorkspaceAsync();
        var home = (await ListAsync(admin)).Single(p => p.IsDefault);
        var other = await CreateAsync(admin, "Other");
        var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads",
            new SaveLeadRequest("Deal", "Maker", null, null, null, "new", "website", null, null, null, null, null))).Content.ReadFromJsonAsync<LeadDto>();
        var convert = await admin.Client.PostAsJsonAsync($"/api/v1/leads/{lead!.Id}/convert",
            new ConvertLeadRequest("create", null, "Deal Co", "create", null, true, "Big deal", home.Id, null, 1000m, null, null));
        convert.StatusCode.ShouldBe(HttpStatusCode.OK, await convert.Content.ReadAsStringAsync());

        var refused = await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{home.Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("pipeline_has_deals");

        var target = other.Stages.OrderBy(s => s.SortOrder).First();
        (await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{home.Id}?moveDealsToStageId={target.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(admin)).ShouldNotContain(p => p.Id == home.Id);
    }

    [Fact]
    public async Task An_open_stage_can_be_added_before_won_and_lost()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");
        var openCount = pipeline.Stages.Count(s => s.StageType == "Open");

        var response = await AddStageAsync(admin, pipeline.Id, "Proposal", "Open", 60, openCount + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var stages = (await ListAsync(admin)).Single(p => p.Id == pipeline.Id).Stages.OrderBy(s => s.SortOrder).ToList();
        stages.Select(s => s.Name).ShouldContain("Proposal");
        stages.Take(stages.Count - 2).ShouldAllBe(s => s.StageType == "Open");
        stages[^2].StageType.ShouldBe("Won");
        stages[^1].StageType.ShouldBe("Lost");
    }

    private static async Task<List<PipelineStageDto>> StagesAsync(Session admin, Guid pipelineId)
        => (await ListAsync(admin)).Single(p => p.Id == pipelineId).Stages.OrderBy(s => s.SortOrder).ToList();

    [Fact]
    public async Task A_stage_inserted_at_a_position_pushes_the_others_down_and_everything_is_numbered_from_one()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");
        var before = pipeline.Stages.OrderBy(s => s.SortOrder).Select(s => s.Name).ToList();

        (await AddStageAsync(admin, pipeline.Id, "Second", "Open", 20, 2)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stages = await StagesAsync(admin, pipeline.Id);
        stages.Select(s => s.SortOrder).ShouldBe(Enumerable.Range(1, stages.Count));
        stages[0].Name.ShouldBe(before[0]);
        stages[1].Name.ShouldBe("Second");
        stages.Skip(2).Select(s => s.Name).ShouldBe(before.Skip(1));
    }

    [Fact]
    public async Task A_position_past_the_end_puts_the_stage_last_and_a_position_below_one_puts_it_first()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");

        (await AddStageAsync(admin, pipeline.Id, "First open", "Open", 5, 0)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StagesAsync(admin, pipeline.Id))[0].Name.ShouldBe("First open");

        // After Lost is not allowed for an open stage, whatever number was given.
        (await AddStageAsync(admin, pipeline.Id, "Way past", "Open", 5, 999)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await StagesAsync(admin, pipeline.Id)).ShouldNotContain(s => s.Name == "Way past");
    }

    [Fact]
    public async Task Moving_a_stage_to_a_new_position_reorders_the_rest()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");
        await AddStageAsync(admin, pipeline.Id, "Extra", "Open", 30, 2);
        var stages = await StagesAsync(admin, pipeline.Id);
        var extra = stages.Single(s => s.Name == "Extra");

        var response = await admin.Client.PutAsJsonAsync($"/api/v1/settings/pipelines/{pipeline.Id}/stages/{extra.Id}",
            new SaveStageRequest("Extra", null, 30, "Open", null, null, 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var after = await StagesAsync(admin, pipeline.Id);
        after[0].Name.ShouldBe("Extra");
        after.Select(s => s.SortOrder).ShouldBe(Enumerable.Range(1, after.Count));
        after.Select(s => s.Name).Skip(1).ShouldBe(stages.Where(s => s.Name != "Extra").Select(s => s.Name));
    }

    [Fact]
    public async Task Deleting_a_stage_closes_the_gap()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");
        await AddStageAsync(admin, pipeline.Id, "Extra", "Open", 30, 2);
        var extra = (await StagesAsync(admin, pipeline.Id)).Single(s => s.Name == "Extra");

        (await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{pipeline.Id}/stages/{extra.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await StagesAsync(admin, pipeline.Id);
        after.Select(s => s.SortOrder).ShouldBe(Enumerable.Range(1, after.Count));
    }

    [Fact]
    public async Task Setup_cannot_be_finished_without_a_pipeline_and_can_once_there_is_one()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var refused = await admin.Client.PostAsync("/api/v1/onboarding/complete", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("setup_incomplete");

        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Client.PostAsync("/api/v1/onboarding/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_second_won_stage_is_refused()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");

        var response = await AddStageAsync(admin, pipeline.Id, "Also won", "Won", 100, pipeline.Stages.Count + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("pipeline_rules");
    }

    [Fact]
    public async Task An_open_stage_placed_after_won_is_refused()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");

        var response = await AddStageAsync(admin, pipeline.Id, "Too late", "Open", 50, pipeline.Stages.Count + 5);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task A_probability_outside_zero_to_one_hundred_is_refused(int probability)
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");

        var response = await AddStageAsync(admin, pipeline.Id, "Odd", "Open", probability, 2);

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task An_unknown_stage_type_is_refused()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");

        var response = await AddStageAsync(admin, pipeline.Id, "Maybe", "Maybe", 50, 2);

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task The_last_open_stage_cannot_be_deleted()
    {
        var admin = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Renewals");
        var opens = pipeline.Stages.Where(s => s.StageType == "Open").ToList();

        // Remove every open stage but the last. The last one is protected by the pipeline rules.
        foreach (var stage in opens.Skip(1))
        {
            (await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{pipeline.Id}/stages/{stage.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var response = await admin.Client.DeleteAsync($"/api/v1/settings/pipelines/{pipeline.Id}/stages/{opens[0].Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("pipeline_rules");
    }

    [Fact]
    public async Task Another_workspace_cannot_touch_a_pipeline_or_its_stages()
    {
        var admin = await NewWorkspaceAsync();
        var other = await NewWorkspaceAsync();
        var pipeline = await CreateAsync(admin, "Private");

        (await other.Client.PutAsJsonAsync($"/api/v1/settings/pipelines/{pipeline.Id}", new SavePipelineRequest("Hijacked", null, false, true, 1))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.DeleteAsync($"/api/v1/settings/pipelines/{pipeline.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AddStageAsync(other, pipeline.Id, "Sneaky", "Open", 10, 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(other)).ShouldNotContain(p => p.Id == pipeline.Id);
    }
}

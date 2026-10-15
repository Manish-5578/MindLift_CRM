using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Imports;
using Shouldly;

namespace MindLift.IntegrationTests.Deals;

/// <summary>Deals: create, board, stage changes (Open, Won, Lost), history, rotting, currencies, contacts, import and export.</summary>
public sealed class DealTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed record Setup(Session Session, PipelineDto Pipeline)
    {
        public PipelineStageDto Open(int index = 0) => Pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).ElementAt(index);
        public PipelineStageDto Won => Pipeline.Stages.First(s => s.StageType == "Won");
        public PipelineStageDto Lost => Pipeline.Stages.First(s => s.StageType == "Lost");
    }

    private async Task<Setup> StartAsync()
    {
        var session = await Workspaces.SignupAsync(factory);
        (await session.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipelines = (await session.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        return new Setup(session, pipelines.First(p => p.IsDefault));
    }

    private static SaveDealRequest NewDeal(string name, decimal amount = 1000, Guid? stageId = null, string? currency = null, Guid? accountId = null, IReadOnlyList<DealContactInput>? contacts = null, int? probability = null)
        => new(name, null, stageId, amount, currency, probability, null, accountId, contacts, null, null, null);

    private static async Task<T> SendAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<DealDto> CreateAsync(Setup s, SaveDealRequest request) => s.Session.Client.PostAsJsonAsync("/api/v1/deals", request).ContinueWith(t => SendAsync<DealDto>(t.Result)).Unwrap();

    private static Task<DealDto> MoveAsync(Setup s, Guid id, PipelineStageDto stage, string? lostReason = null, int? version = null)
        => s.Session.Client.PatchAsJsonAsync($"/api/v1/deals/{id}/stage", new ChangeDealStageRequest(stage.Id, lostReason, null, null, version)).ContinueWith(t => SendAsync<DealDto>(t.Result)).Unwrap();

    [Fact]
    public async Task A_new_deal_starts_in_the_first_open_stage_with_that_stages_probability()
    {
        var s = await StartAsync();

        var deal = await CreateAsync(s, NewDeal("First deal"));

        deal.StageId.ShouldBe(s.Open().Id);
        deal.Status.ShouldBe("Open");
        deal.Probability.ShouldBe(s.Open().Probability);
        deal.ProbabilityOverridden.ShouldBeFalse();
        deal.PipelineId.ShouldBe(s.Pipeline.Id);
    }

    [Fact]
    public async Task A_deal_needs_a_name_a_sensible_amount_and_an_open_starting_stage()
    {
        var s = await StartAsync();

        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Negative", -1))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Bad probability", probability: 101))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal(new string('x', 201)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Starts won", stageId: s.Won.Id))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Unknown stage", stageId: Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Moving_between_open_stages_keeps_a_typed_probability_but_otherwise_follows_the_stage()
    {
        var s = await StartAsync();
        var follows = await CreateAsync(s, NewDeal("Follows"));
        var typed = await CreateAsync(s, NewDeal("Typed", probability: 33));
        typed.ProbabilityOverridden.ShouldBeTrue();

        (await MoveAsync(s, follows.Id, s.Open(1))).Probability.ShouldBe(s.Open(1).Probability);
        (await MoveAsync(s, typed.Id, s.Open(1))).Probability.ShouldBe(33);
    }

    [Fact]
    public async Task Winning_sets_the_status_closed_date_and_full_probability_and_reopening_clears_them()
    {
        var s = await StartAsync();
        var deal = await CreateAsync(s, NewDeal("Win me"));

        var won = await MoveAsync(s, deal.Id, s.Won);
        won.Status.ShouldBe("Won");
        won.Probability.ShouldBe(100);
        won.ClosedAtUtc.ShouldNotBeNull();

        var reopened = await MoveAsync(s, deal.Id, s.Open());
        reopened.Status.ShouldBe("Open");
        reopened.ClosedAtUtc.ShouldBeNull();
        reopened.Probability.ShouldBe(s.Open().Probability);
    }

    [Fact]
    public async Task Losing_needs_a_valid_reason_and_zeroes_the_probability()
    {
        var s = await StartAsync();
        var deal = await CreateAsync(s, NewDeal("Lose me"));

        var missing = await s.Session.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(s.Lost.Id, null, null, null, null));
        missing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("lost_reason_required");
        (await s.Session.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(s.Lost.Id, "nonsense", null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var lost = await MoveAsync(s, deal.Id, s.Lost, "price");
        lost.Status.ShouldBe("Lost");
        lost.LostReason.ShouldBe("price");
        lost.Probability.ShouldBe(0);
    }

    [Fact]
    public async Task A_stage_from_another_pipeline_is_refused()
    {
        var s = await StartAsync();
        var other = await SendAsync<PipelineDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Other", null, false, true, 5)));
        var stage = await SendAsync<PipelineStageDto>(await s.Session.Client.PostAsJsonAsync($"/api/v1/settings/pipelines/{other.Id}/stages", new SaveStageRequest("Start", null, 10, "Open", null, null, 1)));
        var deal = await CreateAsync(s, NewDeal("Stays put"));

        var response = await s.Session.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(stage.Id, null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("stage_not_in_pipeline");
    }

    [Fact]
    public async Task Every_move_is_written_to_the_history_newest_first()
    {
        var s = await StartAsync();
        var deal = await CreateAsync(s, NewDeal("Tracked"));
        await MoveAsync(s, deal.Id, s.Open(1));
        await MoveAsync(s, deal.Id, s.Won);

        var history = (await s.Session.Client.GetFromJsonAsync<List<DealStageHistoryDto>>($"/api/v1/deals/{deal.Id}/history"))!;

        history.Count.ShouldBe(3);
        history[0].ToStage.ShouldBe(s.Won.Name);
        history[0].FromStage.ShouldBe(s.Open(1).Name);
        history[0].DaysInFromStage.ShouldNotBeNull();
        history[2].FromStage.ShouldBeNull();
        history[2].ToStage.ShouldBe(s.Open().Name);
    }

    [Fact]
    public async Task A_stale_version_gets_a_conflict_so_the_board_reloads()
    {
        var s = await StartAsync();
        var deal = await CreateAsync(s, NewDeal("Raced"));
        await MoveAsync(s, deal.Id, s.Open(1));

        var response = await s.Session.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(s.Open(2).Id, null, null, null, deal.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_board_has_a_column_per_stage_with_counts_totals_and_weighted_totals()
    {
        var s = await StartAsync();
        await CreateAsync(s, NewDeal("A", 1000));
        await CreateAsync(s, NewDeal("B", 3000));
        var won = await CreateAsync(s, NewDeal("C", 500));
        await MoveAsync(s, won.Id, s.Won);

        var board = await SendAsync<DealBoardDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/deals/board", new { }));

        board.Stages.Select(c => c.Id).ShouldBe(s.Pipeline.Stages.OrderBy(x => x.SortOrder).Select(x => x.Id));
        var first = board.Stages.First(c => c.Id == s.Open().Id);
        first.Count.ShouldBe(2);
        first.TotalInBase.ShouldBe(4000m);
        first.WeightedInBase.ShouldBe(4000m * s.Open().Probability / 100m);
        first.Deals[0].Name.ShouldBe("B");
        board.Stages.First(c => c.Id == s.Won.Id).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Amounts_in_another_currency_are_shown_converted_to_the_base_currency()
    {
        var s = await StartAsync();
        var settings = await s.Session.Client.GetFromJsonAsync<CompanySettingsDto>("/api/v1/settings/company");
        var other = settings!.BaseCurrency == "EUR" ? "GBP" : "EUR";

        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Foreign", 100, currency: other))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await SendAsync<CurrencySettingsDto>(await s.Session.Client.PutAsJsonAsync("/api/v1/settings/currencies", new SaveCurrencySettingsRequest(settings.BaseCurrency, [settings.BaseCurrency, other])));
        await SendAsync<ExchangeRateDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/settings/exchange-rates", new SaveExchangeRateRequest(other, 2.5m, DateTime.UtcNow.Date.AddDays(-1))));
        var deal = await CreateAsync(s, NewDeal("Foreign", 100, currency: other));

        deal.AmountInBase.ShouldBe(250m);
        var board = await SendAsync<DealBoardDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/deals/board", new { }));
        board.Stages.First(c => c.Id == s.Open().Id).TotalInBase.ShouldBe(250m);
        board.BaseCurrency.ShouldBe(settings.BaseCurrency);
    }

    [Fact]
    public async Task A_deal_can_have_several_contacts_with_one_primary_and_the_account_follows_the_primary()
    {
        var s = await StartAsync();
        var account = await SendAsync<AccountDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Acme", null, null, null, null, null, null, null)));
        var first = await SendAsync<ContactDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("One", "Tester", null, null, null, null, account.Id, null, null, null)));
        var second = await SendAsync<ContactDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Two", "Tester", null, null, null, null, null, null, null, null)));

        var deal = await CreateAsync(s, NewDeal("Many", contacts: [new(first.Id, "Buyer", true), new(second.Id, "Signer", false)]));

        deal.Contacts.Count.ShouldBe(2);
        deal.Contacts[0].IsPrimary.ShouldBeTrue();
        deal.Contacts[0].FullName.ShouldBe("One Tester");
        deal.AccountId.ShouldBe(account.Id);

        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Two primaries", contacts: [new(first.Id, null, true), new(second.Id, null, true)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Session.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Ghost", contacts: [new(Guid.NewGuid(), null, true)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Winning_a_deal_turns_its_prospect_contact_into_a_client_and_the_contact_page_lists_the_deal()
    {
        var s = await StartAsync();
        var contact = await SendAsync<ContactDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Pro", "Spect", null, null, null, null, null, "prospect", null, null)));
        var deal = await CreateAsync(s, NewDeal("Promote", contacts: [new(contact.Id, null, true)]));

        await MoveAsync(s, deal.Id, s.Won);

        (await s.Session.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!.Type.ShouldBe("client");
        var summary = await s.Session.Client.GetFromJsonAsync<ContactSummaryDto>($"/api/v1/contacts/{contact.Id}/summary");
        summary!.Deals.Select(d => d.Name).ShouldBe(["Promote"]);
    }

    [Fact]
    public async Task A_deal_with_no_movement_for_longer_than_the_stage_allows_is_rotting()
    {
        var s = await StartAsync();
        var stage = s.Open();
        await SendAsync<PipelineStageDto>(await s.Session.Client.PutAsJsonAsync($"/api/v1/settings/pipelines/{s.Pipeline.Id}/stages/{stage.Id}",
            new SaveStageRequest(stage.Name, stage.NameAr, stage.Probability, "Open", stage.Color, 1, stage.SortOrder)));
        var deal = await CreateAsync(s, NewDeal("Fresh"));

        deal.IsRotting.ShouldBeFalse();
        // Age the deal directly: nothing in the API can move time backwards.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MindLift.Infrastructure.Persistence.AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE deals SET stage_changed_at = now() - interval '5 days' WHERE id = {0}", deal.Id);
        }

        (await s.Session.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!.IsRotting.ShouldBeTrue();
        var board = await SendAsync<DealBoardDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/deals/board", new { }));
        board.Stages.SelectMany(c => c.Deals).Single(d => d.Id == deal.Id).IsRotting.ShouldBeTrue();
    }

    [Fact]
    public async Task The_list_searches_by_deal_and_company_name_and_pages()
    {
        var s = await StartAsync();
        var account = await SendAsync<AccountDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Zenith Group", null, null, null, null, null, null, null)));
        await CreateAsync(s, NewDeal("Alpha", accountId: account.Id));
        await CreateAsync(s, NewDeal("Beta"));

        var byCompany = await SendAsync<PagedResult<DealListItemDto>>(await s.Session.Client.PostAsJsonAsync("/api/v1/deals/query", new ListQuery("zenith", null, null, null)));
        byCompany.Items.Select(d => d.Name).ShouldBe(["Alpha"]);

        var paged = await SendAsync<PagedResult<DealListItemDto>>(await s.Session.Client.PostAsJsonAsync("/api/v1/deals/query", new ListQuery(null, null, "name", "asc", 2, 1)));
        paged.Total.ShouldBe(2);
        paged.Items.Single().Name.ShouldBe("Beta");
    }

    [Fact]
    public async Task Changing_a_deals_pipeline_moves_it_to_a_stage_of_the_new_pipeline()
    {
        var s = await StartAsync();
        var other = await SendAsync<PipelineDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Renewals", null, false, true, 5)));
        var stage = await SendAsync<PipelineStageDto>(await s.Session.Client.PostAsJsonAsync($"/api/v1/settings/pipelines/{other.Id}/stages", new SaveStageRequest("Reach out", null, 20, "Open", null, null, 1)));
        var deal = await CreateAsync(s, NewDeal("Mover"));

        var moved = await SendAsync<DealDto>(await s.Session.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}", NewDeal("Mover") with { PipelineId = other.Id, StageId = stage.Id }));

        moved.PipelineId.ShouldBe(other.Id);
        moved.StageId.ShouldBe(stage.Id);
        moved.Probability.ShouldBe(20);
    }

    [Fact]
    public async Task Deals_export_and_import_round_trip_and_skip_open_deals_with_the_same_name()
    {
        var s = await StartAsync();
        await CreateAsync(s, NewDeal("Existing", 500));

        var csv = "Name,Amount,Stage,Expected close\nExisting,1,,\nImported,2500,,2027-01-31\n,5,,\n";
        var result = await SendAsync<ImportResultDto>(await s.Session.Client.PostAsync("/api/v1/deals/import", new StringContent(csv, Encoding.UTF8, "text/csv")));
        (result.Created, result.Skipped, result.Failed).ShouldBe((1, 1, 1));

        var export = await s.Session.Client.PostAsJsonAsync("/api/v1/deals/export", new ListQuery(null, null, "name", "asc", 1, 200));
        var rows = CsvText.Parse((await export.Content.ReadAsStringAsync()).TrimStart('﻿'));
        rows[0].Take(3).ShouldBe(["id", "name", "amount"]);
        rows.Skip(1).Select(r => r[1]).ShouldBe(["Existing", "Imported"]);
        rows.Single(r => r[1] == "Imported")[rows[0].IndexOf("expected_close_date")].ShouldBe("2027-01-31");
    }

    [Fact]
    public async Task A_converted_lead_deal_gets_its_contact_and_a_first_history_row()
    {
        var s = await StartAsync();
        var lead = await SendAsync<LeadDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Conv", "Lead", "c@example.test", null, "Conv Co", "new", "website", null, null, null, null, null)));
        var result = await SendAsync<ConvertLeadResult>(await s.Session.Client.PostAsJsonAsync($"/api/v1/leads/{lead.Id}/convert",
            new ConvertLeadRequest("create", null, null, "new", null, true, null, null, null, 900, null, null)));

        var deal = await s.Session.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{result.DealId}");

        deal!.Contacts.Single().IsPrimary.ShouldBeTrue();
        (await s.Session.Client.GetFromJsonAsync<List<DealStageHistoryDto>>($"/api/v1/deals/{result.DealId}/history"))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_deal_removes_it_and_another_workspace_cannot_see_it()
    {
        var s = await StartAsync();
        var other = await Workspaces.SignupAsync(factory);
        var deal = await CreateAsync(s, NewDeal("Private"));

        (await other.Client.GetAsync($"/api/v1/deals/{deal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Session.Client.DeleteAsync($"/api/v1/deals/{deal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Session.Client.GetAsync($"/api/v1/deals/{deal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_board_carries_the_arabic_name_of_each_stage()
    {
        var s = await StartAsync();

        var board = await SendAsync<DealBoardDto>(await s.Session.Client.PostAsJsonAsync("/api/v1/deals/board", new { }));

        board.Stages.ShouldAllBe(column => !string.IsNullOrWhiteSpace(column.NameAr));
        foreach (var column in board.Stages)
        {
            column.NameAr.ShouldBe(s.Pipeline.Stages.Single(stage => stage.Id == column.Id).NameAr);
        }
    }

    [Fact]
    public async Task The_stage_history_carries_the_arabic_stage_names()
    {
        var s = await StartAsync();
        var deal = await CreateAsync(s, NewDeal("History deal", 1000));
        await MoveAsync(s, deal.Id, s.Won);

        var history = await SendAsync<List<DealStageHistoryDto>>(await s.Session.Client.GetAsync($"/api/v1/deals/{deal.Id}/history"));

        var move = history.First();
        move.ToStageAr.ShouldBe(s.Won.NameAr);
        move.FromStageAr.ShouldBe(s.Open().NameAr);
    }
}

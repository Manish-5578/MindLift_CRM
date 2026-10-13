using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Administration;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Onboarding;

/// <summary>
/// The setup wizard (plan 6.1, 6.3, 6.8). Each step can be revisited. The current step only moves forward,
/// and skipping is allowed for steps 3 to 6.
/// </summary>
public sealed class OnboardingService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    CompanySettingsService company,
    IndustryTemplateService industries,
    SampleDataService samples,
    InvitationService invitations,
    SettingsVersion version)
{
    private static readonly int[] Skippable = [OnboardingSteps.Pipeline, OnboardingSteps.Team, OnboardingSteps.Data, OnboardingSteps.Channels];

    public async Task<OnboardingDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await LoadWorkspaceAsync(cancellationToken);
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);

        var userCount = await db.Users.CountAsync(u => u.IsActive, cancellationToken);
        var realLeads = await db.Leads.AnyAsync(l => !l.IsSample, cancellationToken);
        var realDeals = await db.Deals.AnyAsync(d => !d.IsSample, cancellationToken);

        var checklist = new List<OnboardingChecklistItemDto>
        {
            new("company_logo", "Add your company logo", false, Available: false),
            new("invite_team", "Invite your team", userCount > 1, true),
            new("first_lead", "Add your first lead", realLeads, true),
            new("first_deal", "Create your first deal", realDeals, true),
            new("connect_email", "Connect your email", false, Available: false),
            new("connect_whatsapp", "Connect WhatsApp", false, Available: false),
            new("web_form", "Create a web form", false, Available: false),
            new("sales_target", "Set a sales target", false, Available: false),
            new("remove_sample", "Remove sample data", !settings.SampleDataLoaded, true),
        };

        return new OnboardingDto(
            workspace.OnboardingStep,
            workspace.OnboardingDone,
            workspace.OnboardingSkipped,
            new OnboardingTenantDto(workspace.Name, workspace.Status, workspace.IndustryKey, workspace.TrialEndsAt, settings.SampleDataLoaded),
            checklist,
            workspace.ChecklistDismissed);
    }

    /// <summary>Step 1: company profile. Also sets the country and the base currency.</summary>
    public async Task<OnboardingDto> SaveCompanyAsync(OnboardingCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var current = await company.GetCompanyAsync(cancellationToken);
        var currencies = await company.GetCurrenciesAsync(cancellationToken);

        await company.UpdateCompanyAsync(new SaveCompanySettingsRequest(
            request.CompanyName, current.Email, current.Phone, request.DefaultLanguage, request.TimeZoneId,
            current.WeekStartDay, current.FiscalYearStartMonth, request.TaxEnabled, request.TaxLabel,
            request.DefaultTaxRate, request.TaxRegistrationNumber, current.PrimaryColor), cancellationToken);

        await company.UpdateCurrenciesAsync(new SaveCurrencySettingsRequest(request.BaseCurrency, currencies.EnabledCurrencies), cancellationToken);

        var workspace = await LoadWorkspaceAsync(cancellationToken);
        workspace.Name = request.CompanyName.Trim();
        workspace.Country = request.Country.ToUpperInvariant();
        await db.SaveChangesAsync(cancellationToken);

        return await FinishStepAsync(OnboardingSteps.Company, cancellationToken);
    }

    public IReadOnlyList<IndustrySummaryDto> ListIndustries() => industries.List();

    /// <summary>Step 2: applies an industry template. Can be repeated until real records exist.</summary>
    public async Task<OnboardingDto> ApplyIndustryAsync(string industryKey, CancellationToken cancellationToken = default)
    {
        await industries.ApplyAsync(industryKey, cancellationToken);
        return await FinishStepAsync(OnboardingSteps.Industry, cancellationToken);
    }

    /// <summary>Step 3: the default pipeline's stages, in order. Removing a stage that holds deals is refused.</summary>
    public async Task<OnboardingDto> SavePipelineAsync(SaveOnboardingPipelineRequest request, CancellationToken cancellationToken = default)
    {
        var problem = PipelineRules.Validate(request.Stages.Select(s => (s.StageType, s.Probability)).ToList());
        if (problem is not null)
        {
            throw new BusinessRuleException("pipeline_rules", problem);
        }

        var pipeline = await db.Pipelines.Where(p => p.IsDefault).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("no_pipeline", "No default pipeline exists yet. Apply an industry first.");
        var existing = await db.PipelineStages.Where(s => s.PipelineId == pipeline.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        for (var i = 0; i < request.Stages.Count; i++)
        {
            var wanted = request.Stages[i];
            if (i < existing.Count)
            {
                existing[i].Name = wanted.Name.Trim();
                existing[i].Probability = wanted.Probability;
                existing[i].StageType = wanted.StageType;
                existing[i].SortOrder = i;
            }
            else
            {
                db.PipelineStages.Add(new PipelineStage
                {
                    PipelineId = pipeline.Id,
                    Name = wanted.Name.Trim(),
                    Probability = wanted.Probability,
                    StageType = wanted.StageType,
                    SortOrder = i,
                });
            }
        }

        for (var i = request.Stages.Count; i < existing.Count; i++)
        {
            var stageId = existing[i].Id;
            if (await db.Deals.AnyAsync(d => d.StageId == stageId, cancellationToken))
            {
                throw new ConflictException("stage_has_deals", "A stage you removed still has deals. Keep the stage or move its deals first.");
            }

            db.PipelineStages.Remove(existing[i]);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return await FinishStepAsync(OnboardingSteps.Pipeline, cancellationToken);
    }

    /// <summary>Step 4: invites the team. Each invitation uses a seat, so the plan limit applies.</summary>
    public async Task<OnboardingDto> InviteTeamAsync(OnboardingTeamRequest request, CancellationToken cancellationToken = default)
    {
        foreach (var invite in request.Invites)
        {
            await invitations.CreateAsync(new CreateInvitationRequest(invite.Email, invite.FirstName, invite.LastName, invite.RoleId, invite.TeamId), cancellationToken);
        }

        return await FinishStepAsync(OnboardingSteps.Team, cancellationToken);
    }

    /// <summary>Step 5: sample records, or the import wizard (handled by the UI), or an empty workspace.</summary>
    public async Task<OnboardingDto> ChooseDataAsync(string mode, CancellationToken cancellationToken = default)
    {
        switch (mode)
        {
            case "sample":
                await samples.LoadAsync(cancellationToken);
                break;
            case "import":
            case "empty":
                break;
            default:
                throw new ValidationException(new Dictionary<string, string[]> { ["mode"] = ["Choose sample, import or empty."] });
        }

        return await FinishStepAsync(OnboardingSteps.Data, cancellationToken);
    }

    public async Task<OnboardingDto> SkipAsync(int step, CancellationToken cancellationToken = default)
    {
        if (!Skippable.Contains(step))
        {
            throw new BusinessRuleException("not_skippable", "This step cannot be skipped.");
        }

        var workspace = await LoadWorkspaceAsync(cancellationToken);
        if (!workspace.OnboardingSkipped.Contains(step))
        {
            workspace.OnboardingSkipped = [.. workspace.OnboardingSkipped, step];
        }

        AdvancePast(workspace, step);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<OnboardingDto> CompleteAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await LoadWorkspaceAsync(cancellationToken);

        // The industry step is what creates the first pipeline. Without one, deals have nowhere to go.
        if (!await db.Pipelines.AnyAsync(cancellationToken))
        {
            throw new BusinessRuleException("setup_incomplete", "Choose your industry before finishing setup. It creates your first pipeline.");
        }

        workspace.OnboardingStep = OnboardingSteps.Completed;
        workspace.OnboardingCompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task DismissChecklistAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await LoadWorkspaceAsync(cancellationToken);
        workspace.ChecklistDismissed = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<SampleDataDto> RemoveSampleDataAsync(CancellationToken cancellationToken = default)
        => samples.RemoveAsync(cancellationToken);

    private async Task<OnboardingDto> FinishStepAsync(int step, CancellationToken cancellationToken)
    {
        var workspace = await LoadWorkspaceAsync(cancellationToken);
        if (!workspace.OnboardingDone.Contains(step))
        {
            workspace.OnboardingDone = [.. workspace.OnboardingDone, step];
        }

        AdvancePast(workspace, step);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Moves the current step past <paramref name="step"/>, but never backwards. The last step waits for an explicit complete.</summary>
    private static void AdvancePast(Tenant workspace, int step)
    {
        if (workspace.OnboardingStep <= step)
        {
            workspace.OnboardingStep = Math.Min(step + 1, OnboardingSteps.Channels);
        }
    }

    private async Task<Tenant> LoadWorkspaceAsync(CancellationToken cancellationToken)
        => await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenant.TenantId, cancellationToken)
           ?? throw new NotFoundException("Workspace", tenant.TenantId);
}

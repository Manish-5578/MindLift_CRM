using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Analytics;

public sealed class CustomerHealthService(
    AppDbContext db,
    ITenantContext tenantContext,
    ITimelineEventBus timeline) : ICustomerHealthService
{
    public async Task<CustomerSurveyDto> SubmitSurveyAsync(SubmitSurveyRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var type = (request.Type ?? "NPS").Trim().ToUpperInvariant();
        if (type is not ("NPS" or "CSAT" or "CES"))
            throw new ValidationException("type", "Survey type must be NPS, CSAT, or CES.");

        var score = request.Score;
        string sentiment;

        switch (type)
        {
            case "NPS":
                if (score is < 0 or > 10)
                    throw new ValidationException("score", "NPS score must be between 0 and 10.");
                sentiment = score >= 9 ? "Promoter" : score >= 7 ? "Passive" : "Detractor";
                break;
            case "CSAT":
                if (score is < 1 or > 5)
                    throw new ValidationException("score", "CSAT score must be between 1 and 5.");
                sentiment = score >= 4 ? "Satisfied" : score == 3 ? "Neutral" : "Dissatisfied";
                break;
            case "CES":
                if (score is < 1 or > 7)
                    throw new ValidationException("score", "CES score must be between 1 and 7.");
                sentiment = score >= 5 ? "Low Effort" : score == 4 ? "Moderate Effort" : "High Effort";
                break;
            default:
                sentiment = "Neutral";
                break;
        }

        Guid? accountId = request.AccountId;
        Guid? contactId = request.ContactId;
        Guid? dealId = request.DealId;
        Guid? ticketId = request.TicketId;

        // Autolink contact / account by contactId
        if (contactId.HasValue)
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId.Value, ct);
            if (contact != null && accountId == null)
            {
                accountId = contact.AccountId;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            var normEmail = request.CustomerEmail.Trim().ToLowerInvariant();
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.NormalizedEmail == normEmail || c.Email == normEmail, ct);
            if (contact != null)
            {
                contactId = contact.Id;
                accountId ??= contact.AccountId;
            }
        }

        // Validate account if specified
        if (accountId.HasValue)
        {
            var accountExists = await db.Accounts.AnyAsync(a => a.Id == accountId.Value, ct);
            if (!accountExists)
                throw new NotFoundException("account_not_found", "Associated account was not found.");
        }

        // Validate ticket if specified
        if (ticketId.HasValue)
        {
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId.Value, ct);
            if (ticket != null)
            {
                accountId ??= ticket.AccountId;
                contactId ??= ticket.ContactId;
            }
        }

        var survey = new CustomerSurvey
        {
            TenantId = tenantId,
            Type = type,
            Score = score,
            Sentiment = sentiment,
            Comment = request.Comment,
            Channel = request.Channel ?? "web",
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail?.Trim().ToLowerInvariant(),
            CustomerPhone = request.CustomerPhone,
            AccountId = accountId,
            ContactId = contactId,
            TicketId = ticketId,
            DealId = dealId,
            SubmittedAtUtc = DateTime.UtcNow
        };

        db.CustomerSurveys.Add(survey);
        await db.SaveChangesAsync(ct);

        // Publish timeline events
        try
        {
            var eventSummary = $"{type} survey received: Score {score} ({sentiment})";
            if (contactId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput("contact", contactId.Value, "survey_submitted", eventSummary, "customer-health"), ct);
            }
            if (accountId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput("account", accountId.Value, "survey_submitted", eventSummary, "customer-health"), ct);
            }
        }
        catch
        {
            // Non-fatal
        }

        var accName = accountId.HasValue
            ? await db.Accounts.Where(a => a.Id == accountId.Value).Select(a => a.Name).FirstOrDefaultAsync(ct)
            : null;

        var contactName = contactId.HasValue
            ? await db.Contacts.Where(c => c.Id == contactId.Value).Select(c => $"{c.FirstName} {c.LastName}".Trim()).FirstOrDefaultAsync(ct)
            : null;

        return new CustomerSurveyDto(
            survey.Id,
            survey.Type,
            survey.Score,
            survey.Sentiment,
            survey.Comment,
            survey.Channel,
            survey.CustomerName,
            survey.CustomerEmail,
            survey.CustomerPhone,
            survey.AccountId,
            accName,
            survey.ContactId,
            contactName,
            survey.TicketId,
            survey.DealId,
            survey.SubmittedAtUtc);
    }

    public async Task<IReadOnlyList<CustomerSurveyDto>> GetSurveysAsync(string? type = null, Guid? accountId = null, Guid? contactId = null, int limit = 100, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var query = db.CustomerSurveys.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(type))
        {
            var norm = type.Trim().ToUpperInvariant();
            query = query.Where(s => s.Type == norm);
        }

        if (accountId.HasValue)
        {
            query = query.Where(s => s.AccountId == accountId.Value);
        }

        if (contactId.HasValue)
        {
            query = query.Where(s => s.ContactId == contactId.Value);
        }

        var list = await query
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        var accIds = list.Where(s => s.AccountId != null).Select(s => s.AccountId!.Value).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var conIds = list.Where(s => s.ContactId != null).Select(s => s.ContactId!.Value).Distinct().ToList();
        var contacts = await db.Contacts.AsNoTracking().Where(c => conIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim(), ct);

        return list.Select(s => new CustomerSurveyDto(
            s.Id,
            s.Type,
            s.Score,
            s.Sentiment,
            s.Comment,
            s.Channel,
            s.CustomerName,
            s.CustomerEmail,
            s.CustomerPhone,
            s.AccountId,
            s.AccountId.HasValue ? accounts.GetValueOrDefault(s.AccountId.Value) : null,
            s.ContactId,
            s.ContactId.HasValue ? contacts.GetValueOrDefault(s.ContactId.Value) : null,
            s.TicketId,
            s.DealId,
            s.SubmittedAtUtc)).ToList();
    }

    public async Task<SurveySummaryDto> GetSurveySummaryAsync(string? type = null, Guid? accountId = null, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var normType = (type ?? "NPS").Trim().ToUpperInvariant();
        var query = db.CustomerSurveys.AsNoTracking().Where(s => s.Type == normType);

        if (accountId.HasValue)
        {
            query = query.Where(s => s.AccountId == accountId.Value);
        }

        var surveys = await query.ToListAsync(ct);
        var total = surveys.Count;

        if (total == 0)
        {
            return new SurveySummaryDto(normType, 0, 0m, 0, 0, 0, 0m, 0, 0, 0, 0m);
        }

        var avgScore = Math.Round((decimal)surveys.Average(s => s.Score), 1);

        var promoters = surveys.Count(s => s.Score >= 9);
        var passives = surveys.Count(s => s.Score is 7 or 8);
        var detractors = surveys.Count(s => s.Score <= 6);
        var nps = Math.Round(((decimal)promoters - detractors) / total * 100m, 1);

        var satisfied = surveys.Count(s => s.Score >= 4);
        var neutral = surveys.Count(s => s.Score == 3);
        var dissatisfied = surveys.Count(s => s.Score <= 2);
        var csatPct = Math.Round((decimal)satisfied / total * 100m, 1);

        return new SurveySummaryDto(
            normType,
            total,
            avgScore,
            promoters,
            passives,
            detractors,
            nps,
            satisfied,
            neutral,
            dissatisfied,
            csatPct);
    }

    public async Task<AccountHealthScoreDto> GetAccountHealthAsync(Guid accountId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account == null)
            throw new NotFoundException("account_not_found", "Account was not found.");

        // Component 1: Satisfaction (Surveys) - weight 30%
        var surveys = await db.CustomerSurveys.AsNoTracking()
            .Where(s => s.AccountId == accountId)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .ToListAsync(ct);

        var npsSurveys = surveys.Where(s => s.Type == "NPS").ToList();
        decimal? avgNps = npsSurveys.Count > 0
            ? Math.Round((decimal)npsSurveys.Average(s => s.Score), 1)
            : null;

        int npsComp;
        if (npsSurveys.Count > 0)
        {
            var p = npsSurveys.Count(s => s.Score >= 9);
            var d = npsSurveys.Count(s => s.Score <= 6);
            var npsVal = ((decimal)p - d) / npsSurveys.Count * 100m;
            npsComp = (int)Math.Clamp((npsVal + 100m) / 2m, 0m, 100m);
        }
        else
        {
            npsComp = 75; // Neutral default
        }

        // Component 2: Support Health - weight 25%
        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Status != "closed")
            .ToListAsync(ct);

        var openTicketsCount = tickets.Count;
        var urgentTicketsCount = tickets.Count(t => t.Priority is "urgent" or "high");

        var supportComp = Math.Clamp(100 - (openTicketsCount * 10) - (urgentTicketsCount * 20), 0, 100);

        // Component 3: Engagement (Activities in last 90 days) - weight 25%
        var ninetyDaysAgo = DateTime.UtcNow.AddDays(-90);
        var recentActivitiesCount = await db.Activities.AsNoTracking()
            .Where(a => a.AccountId == accountId && a.CreatedAt >= ninetyDaysAgo)
            .CountAsync(ct);

        int engagementComp = recentActivitiesCount >= 5 ? 100 :
                             recentActivitiesCount >= 3 ? 80 :
                             recentActivitiesCount >= 1 ? 60 : 30;

        // Component 4: Deal Momentum - weight 20%
        var deals = await db.Deals.AsNoTracking()
            .Where(d => d.AccountId == accountId)
            .ToListAsync(ct);

        var wonDeals = deals.Where(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).ToList();
        var openDeals = deals.Where(d => d.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)).ToList();

        var wonDealsCount = wonDeals.Count;
        var wonDealsValue = wonDeals.Sum(d => d.Amount);
        var openDealsValue = openDeals.Sum(d => d.Amount);

        int dealComp = wonDealsCount > 0 ? 95 :
                       openDeals.Count > 0 ? 75 :
                       deals.Count == 0 ? 60 : 35;

        // Composite Health Score (0-100)
        var healthScore = (int)Math.Round(npsComp * 0.30 + supportComp * 0.25 + engagementComp * 0.25 + dealComp * 0.20);
        healthScore = Math.Clamp(healthScore, 0, 100);

        var healthStatus = healthScore >= 70 ? "Good" : healthScore >= 40 ? "Average" : "Poor";

        var trend = (npsComp >= 70 && supportComp >= 70) ? "Improving" :
                    (healthScore < 50 || urgentTicketsCount > 0) ? "Declining" : "Stable";

        var riskFactors = new List<string>();
        var recommendations = new List<string>();

        if (urgentTicketsCount > 0)
        {
            riskFactors.Add($"Account has {urgentTicketsCount} urgent or high-priority support ticket(s).");
            recommendations.Add("Escalate and resolve open urgent support tickets immediately.");
        }

        if (npsComp < 50)
        {
            riskFactors.Add("Customer satisfaction is in the Detractor zone.");
            recommendations.Add("Schedule an executive check-in to address customer dissatisfaction.");
        }

        if (engagementComp <= 40)
        {
            riskFactors.Add("Low engagement: No activity recorded in the past 90 days.");
            recommendations.Add("Outreach to primary contact for a quarterly business review.");
        }

        if (riskFactors.Count == 0)
        {
            recommendations.Add("Maintain standard cadence and explore expansion opportunities.");
        }

        return new AccountHealthScoreDto(
            AccountId: account.Id,
            AccountName: account.Name,
            HealthScore: healthScore,
            HealthStatus: healthStatus,
            Trend: trend,
            NpsScoreComponent: npsComp,
            SupportHealthComponent: supportComp,
            EngagementComponent: engagementComp,
            DealMomentumComponent: dealComp,
            TotalSurveysCount: surveys.Count,
            AverageNps: avgNps,
            OpenTicketsCount: openTicketsCount,
            UrgentTicketsCount: urgentTicketsCount,
            RecentActivitiesCount: recentActivitiesCount,
            WonDealsCount: wonDealsCount,
            WonDealsValue: wonDealsValue,
            OpenDealsValue: openDealsValue,
            RiskFactors: riskFactors,
            Recommendations: recommendations,
            CalculatedAtUtc: DateTime.UtcNow);
    }

    public async Task<PortfolioHealthOverviewDto> GetPortfolioOverviewAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        if (accounts.Count == 0)
        {
            return new PortfolioHealthOverviewDto(0, 0, 0, 0, 0m, []);
        }

        var scores = new List<AccountHealthScoreDto>();
        foreach (var account in accounts)
        {
            scores.Add(await GetAccountHealthAsync(account.Id, ct));
        }

        var healthy = scores.Count(s => s.HealthStatus == "Good");
        var avg = scores.Count(s => s.HealthStatus == "Average");
        var atRisk = scores.Count(s => s.HealthStatus == "Poor");
        var avgScore = Math.Round((decimal)scores.Average(s => s.HealthScore), 1);

        var topAtRisk = scores
            .Where(s => s.HealthStatus != "Good")
            .OrderBy(s => s.HealthScore)
            .Take(5)
            .ToList();

        return new PortfolioHealthOverviewDto(
            TotalAccounts: accounts.Count,
            HealthyCount: healthy,
            AverageCount: avg,
            AtRiskCount: atRisk,
            AverageHealthScore: avgScore,
            TopAtRiskAccounts: topAtRisk);
    }
}

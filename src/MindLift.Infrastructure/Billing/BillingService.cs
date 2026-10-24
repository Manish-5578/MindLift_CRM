using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Tenancy;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Billing;

/// <summary>
/// What a company pays the platform: its subscription, its invoices, choosing or changing a plan, cancelling, and the payments that keep it
/// working. A gateway (when connected) or the platform owner (until then) reports payments; this class turns them into the company's access.
/// </summary>
public sealed class BillingService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IPaymentProvider provider,
    NumberSequences numbers,
    IEmailSender email,
    INotificationService notifications,
    IConfiguration configuration,
    ILogger<BillingService> logger)
{
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";

    private string Currency => configuration["Billing:Currency"] ?? "USD";
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");

    // ------------------------------------------------------------------ the company's side

    public async Task<BillingOverviewDto> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId;
        var company = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, cancellationToken);
        var subscription = await EnsureSubscriptionAsync(company, cancellationToken);
        var plans = await db.Plans.AsNoTracking().OrderBy(p => p.PriceMonthly).ToListAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => p.Id == subscription.PlanId);
        var pending = subscription.PendingPlanId is { } pendingId ? plans.FirstOrDefault(p => p.Id == pendingId) : null;

        var users = await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId && u.IsActive, cancellationToken);
        var records = await RecordsAsync(tenantId, cancellationToken);
        var invoices = await InvoicesAsync(tenantId, plans, 24, cancellationToken);
        var switchable = ModuleCatalog.Switchable.Count;

        return new BillingOverviewDto(
            subscription.Status,
            plan?.Key ?? string.Empty,
            plan?.Name ?? string.Empty,
            subscription.Interval,
            plan is null ? 0 : PriceOf(plan, subscription.Interval),
            plan?.Currency ?? Currency,
            company.Status == "Trial" ? company.TrialEndsAt : null,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            pending?.Name,
            subscription.PendingInterval,
            users,
            company.MaxUsersOverride ?? plan?.MaxUsers ?? 0,
            records,
            plan?.MaxRecords ?? 0,
            provider.Online,
            provider.DisplayName,
            provider.Online ? null : configuration["Billing:PaymentInstructions"],
            plans.Where(p => p.IsActive || p.Id == subscription.PlanId)
                .Select(p => new BillingPlanOption(p.Key, p.Name, p.Description, p.MaxUsers, p.MaxRecords, p.PriceMonthly, PriceOf(p, Yearly), p.Currency,
                    p.IncludedModules.Length == 0 ? switchable : p.IncludedModules.Length, p.Id == subscription.PlanId)).ToList(),
            invoices);
    }

    /// <summary>
    /// The company chooses a plan. With a gateway, it is sent to the gateway's page to pay. Without one, an invoice is issued to pay by transfer,
    /// and the change happens when the platform owner records the payment.
    /// </summary>
    public async Task<CheckoutResultDto> CheckoutAsync(StartCheckoutRequest request, string returnBaseUrl, CancellationToken cancellationToken = default)
    {
        var interval = NormaliseInterval(request.Interval);
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Key == request.PlanKey && p.IsActive, cancellationToken)
            ?? throw new ValidationException("planKey", "Choose a plan that is on offer.");
        var company = await db.Tenants.SingleAsync(t => t.Id == CurrentTenantId, cancellationToken);
        var subscription = await EnsureSubscriptionAsync(company, cancellationToken, track: true);

        if (provider.Online)
        {
            var owner = await OwnerAsync(company.Id, cancellationToken);
            var url = await provider.CreateCheckoutUrlAsync(new CheckoutContext(company.Id, company.Name, owner?.Email ?? string.Empty, plan.Key, plan.Name, interval,
                PriceOf(plan, interval), plan.Currency, $"{returnBaseUrl}/settings/billing?paid=1", $"{returnBaseUrl}/settings/billing"), cancellationToken);
            return new CheckoutResultDto("redirect", url, null, "Continue to the payment page.");
        }

        // Manual billing: one open invoice for the choice; an earlier unpaid choice is replaced.
        var open = await db.BillingInvoices.Where(i => i.TenantId == company.Id && i.Status == "open" && i.Reference == null).ToListAsync(cancellationToken);
        foreach (var stale in open)
        {
            stale.Status = "void";
        }

        var (start, end) = NextPeriod(subscription, interval);
        var invoice = await NewInvoiceAsync(company.Id, plan, interval, start, end, cancellationToken);
        subscription.PendingPlanId = plan.Id;
        subscription.PendingInterval = interval;
        subscription.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var plans = await db.Plans.AsNoTracking().ToListAsync(cancellationToken);
        return new CheckoutResultDto("invoice", null, Map(invoice, plans),
            $"Invoice {invoice.Number} for {Money(invoice.Amount, invoice.Currency)} is ready. Your {plan.Name} plan starts as soon as the payment is received.");
    }

    /// <summary>Stops at the end of the paid period. Nothing is refunded and the company keeps working until then.</summary>
    public async Task<BillingOverviewDto> CancelAsync(CancellationToken cancellationToken = default)
    {
        var company = await db.Tenants.SingleAsync(t => t.Id == CurrentTenantId, cancellationToken);
        var subscription = await EnsureSubscriptionAsync(company, cancellationToken, track: true);
        if (subscription.Status != "active")
        {
            throw new BusinessRuleException("not_active", "Only an active subscription can be cancelled.");
        }

        subscription.CancelAtPeriodEnd = true;
        subscription.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await OverviewAsync(cancellationToken);
    }

    public async Task<BillingOverviewDto> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var company = await db.Tenants.SingleAsync(t => t.Id == CurrentTenantId, cancellationToken);
        var subscription = await EnsureSubscriptionAsync(company, cancellationToken, track: true);
        subscription.CancelAtPeriodEnd = false;
        subscription.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await OverviewAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ the platform owner's side

    public async Task<PlatformBillingDto> PlatformViewAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var company = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken) ?? throw new NotFoundException("Company", tenantId);
        var subscription = await EnsureSubscriptionAsync(company, cancellationToken);
        var plans = await db.Plans.AsNoTracking().ToListAsync(cancellationToken);
        var invoices = await InvoicesAsync(tenantId, plans, 50, cancellationToken);
        var paid = await db.BillingInvoices.Where(i => i.TenantId == tenantId && i.Status == "paid").SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0;
        return new PlatformBillingDto(
            subscription.Status,
            plans.FirstOrDefault(p => p.Id == subscription.PlanId)?.Name ?? string.Empty,
            subscription.Interval,
            subscription.Provider,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.PendingPlanId is { } p ? plans.FirstOrDefault(x => x.Id == p)?.Name : null,
            subscription.PendingInterval,
            paid,
            Currency,
            invoices);
    }

    /// <summary>
    /// The platform owner records money received. Against an open invoice, that invoice is paid and its plan and period apply.
    /// Without one, a paid invoice is created for the plan and interval given (the company's own plan when none is given).
    /// </summary>
    public async Task<PlatformBillingDto> RecordPaymentAsync(Guid tenantId, RecordBillingPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var company = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken) ?? throw new NotFoundException("Company", tenantId);
        var subscription = await EnsureSubscriptionAsync(company, cancellationToken, track: true);
        var paidAt = request.PaidAt is { } when ? DateTime.SpecifyKind(when, DateTimeKind.Utc) : clock.UtcNow;

        BillingInvoice invoice;
        Plan plan;
        if (request.InvoiceId is { } invoiceId)
        {
            invoice = await db.BillingInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, cancellationToken)
                ?? throw new NotFoundException("Invoice", invoiceId);
            if (invoice.Status != "open")
            {
                throw new BusinessRuleException("invoice_not_open", "Only an open invoice can be marked paid.");
            }

            plan = await db.Plans.SingleAsync(p => p.Id == invoice.PlanId, cancellationToken);
            if (request.Amount is { } amount)
            {
                invoice.Amount = amount;
            }
        }
        else
        {
            var interval = NormaliseInterval(request.Interval ?? subscription.Interval);
            plan = await db.Plans.FirstOrDefaultAsync(p => p.Key == (request.PlanKey ?? string.Empty), cancellationToken)
                ?? await db.Plans.SingleAsync(p => p.Id == subscription.PlanId, cancellationToken);
            var (start, end) = NextPeriod(subscription, interval);
            invoice = await NewInvoiceAsync(tenantId, plan, interval, start, end, cancellationToken);
            if (request.Amount is { } amount)
            {
                invoice.Amount = amount;
            }
        }

        if (invoice.Amount < 0)
        {
            throw new ValidationException("amount", "The amount cannot be negative.");
        }

        invoice.Status = "paid";
        invoice.PaidAt = paidAt;
        invoice.PaidVia = "manual";
        invoice.Reference = string.IsNullOrWhiteSpace(request.Reference) ? "Recorded by the platform owner" : request.Reference.Trim();

        Activate(company, subscription, plan, invoice.Interval, invoice.PeriodStart, invoice.PeriodEnd);
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            TenantId = company.Id,
            Company = company.Name,
            ActorId = tenant.UserId,
            Actor = await ActorAsync(cancellationToken),
            Action = "billing.payment",
            Summary = $"Payment of {Money(invoice.Amount, invoice.Currency)} recorded ({invoice.Number}), {plan.Name} {invoice.Interval} until {invoice.PeriodEnd:dd MMM yyyy}",
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await TellOwnerAsync(company, "Payment received", $"Thank you. {Money(invoice.Amount, invoice.Currency)} was received for invoice {invoice.Number}. Your {plan.Name} plan runs until {invoice.PeriodEnd:dd MMM yyyy}.", cancellationToken);
        return await PlatformViewAsync(tenantId, cancellationToken);
    }

    public async Task<PlatformBillingDto> VoidInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, cancellationToken) ?? throw new NotFoundException("Invoice", invoiceId);
        if (invoice.Status != "open")
        {
            throw new BusinessRuleException("invoice_not_open", "Only an open invoice can be cancelled.");
        }

        invoice.Status = "void";
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (subscription is not null && subscription.PendingPlanId == invoice.PlanId)
        {
            subscription.PendingPlanId = null;
            subscription.PendingInterval = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await PlatformViewAsync(tenantId, cancellationToken);
    }

    // ------------------------------------------------------------------ the gateway's side

    /// <summary>Applies what a gateway reports. Each event is applied once, however many times the gateway sends it.</summary>
    public async Task<int> ApplyAsync(IReadOnlyList<ProviderEvent> events, CancellationToken cancellationToken = default)
    {
        var applied = 0;
        foreach (var e in events)
        {
            if (await db.BillingEvents.AnyAsync(x => x.Provider == provider.Key && x.EventId == e.EventId, cancellationToken))
            {
                continue;
            }

            var subscription = e.TenantId is { } id
                ? await db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == id, cancellationToken)
                : e.ProviderSubscriptionId is { } sid ? await db.Subscriptions.FirstOrDefaultAsync(s => s.ProviderSubscriptionId == sid, cancellationToken) : null;
            var company = subscription is not null ? await db.Tenants.FirstOrDefaultAsync(t => t.Id == subscription.TenantId, cancellationToken)
                : e.TenantId is { } tid ? await db.Tenants.FirstOrDefaultAsync(t => t.Id == tid, cancellationToken) : null;

            db.BillingEvents.Add(new BillingEvent { Provider = provider.Key, EventId = e.EventId, Type = e.Type, TenantId = company?.Id, ReceivedAt = clock.UtcNow, Payload = e.RawPayload });

            if (company is null)
            {
                logger.LogWarning("Billing event {EventId} ({Type}) names no known company", e.EventId, e.Type);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            subscription ??= await EnsureSubscriptionAsync(company, cancellationToken, track: true);
            subscription.Provider = provider.Key;
            subscription.ProviderSubscriptionId = e.ProviderSubscriptionId ?? subscription.ProviderSubscriptionId;
            subscription.ProviderCustomerId = e.ProviderCustomerId ?? subscription.ProviderCustomerId;

            switch (e.Type)
            {
                case "payment_succeeded":
                {
                    var plan = (e.PlanKey is { } key ? await db.Plans.FirstOrDefaultAsync(p => p.Key == key, cancellationToken) : null)
                        ?? await db.Plans.SingleAsync(p => p.Id == subscription.PlanId, cancellationToken);
                    var interval = NormaliseInterval(e.Interval ?? subscription.Interval);
                    var (start, end) = NextPeriod(subscription, interval);
                    if (e.PeriodEnd is { } providerEnd)
                    {
                        end = DateTime.SpecifyKind(providerEnd, DateTimeKind.Utc);
                    }

                    var invoice = await NewInvoiceAsync(company.Id, plan, interval, start, end, cancellationToken);
                    invoice.Amount = e.Amount ?? invoice.Amount;
                    invoice.Currency = e.Currency?.ToUpperInvariant() ?? invoice.Currency;
                    invoice.Status = "paid";
                    invoice.PaidAt = clock.UtcNow;
                    invoice.PaidVia = provider.Key;
                    invoice.ProviderInvoiceId = e.ProviderInvoiceId;
                    invoice.HostedUrl = e.HostedUrl;
                    Activate(company, subscription, plan, interval, start, end);
                    break;
                }

                case "payment_failed":
                    subscription.Status = "past_due";
                    company.Status = "PastDue";
                    await TellOwnerAsync(company, "Payment failed", "We could not take your subscription payment. Please update your payment details on the Billing page to keep making changes.", cancellationToken);
                    break;

                case "subscription_cancelled":
                    subscription.CancelAtPeriodEnd = true;
                    if (subscription.CurrentPeriodEnd is null || subscription.CurrentPeriodEnd <= clock.UtcNow)
                    {
                        subscription.Status = "cancelled";
                        company.Status = "Cancelled";
                    }

                    break;
            }

            subscription.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            applied++;
        }

        return applied;
    }

    // ------------------------------------------------------------------ renewals (daily job)

    /// <summary>
    /// Once a day: issues renewal invoices a week ahead (manual billing), moves a company whose paid period ended more than the grace period ago
    /// to PastDue, and ends a cancelled subscription when its period is over.
    /// </summary>
    public async Task<(int Invoiced, int PastDue, int Ended)> RunRenewalsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var grace = TimeSpan.FromDays(configuration.GetValue<int?>("Billing:GraceDays") ?? 7);
        var subscriptions = await db.Subscriptions.Where(s => s.Status == "active" && s.CurrentPeriodEnd != null).ToListAsync(cancellationToken);
        var plans = await db.Plans.AsNoTracking().ToListAsync(cancellationToken);
        int invoiced = 0, pastDue = 0, ended = 0;

        foreach (var s in subscriptions)
        {
            var company = await db.Tenants.FirstOrDefaultAsync(t => t.Id == s.TenantId, cancellationToken);
            if (company is null || company.Slug == MindLift.Infrastructure.Setup.PlatformAdminSeeder.PlatformSlug)
            {
                continue;
            }

            var end = s.CurrentPeriodEnd!.Value;
            if (s.CancelAtPeriodEnd)
            {
                if (end <= now)
                {
                    s.Status = "cancelled";
                    company.Status = "Cancelled";
                    ended++;
                }

                continue;
            }

            if (s.Provider == "manual" && end - now <= TimeSpan.FromDays(7) && end > now
                && !await db.BillingInvoices.AnyAsync(i => i.TenantId == s.TenantId && i.Status == "open" && i.PeriodStart >= end.AddDays(-1), cancellationToken))
            {
                var plan = plans.First(p => p.Id == s.PlanId);
                var invoice = await NewInvoiceAsync(s.TenantId, plan, s.Interval, end, Advance(end, s.Interval), cancellationToken);
                invoice.DueAt = end;
                invoiced++;
                await db.SaveChangesAsync(cancellationToken);
                await TellOwnerAsync(company, $"Renewal invoice {invoice.Number}",
                    $"Your {plan.Name} plan renews on {end:dd MMM yyyy}. Invoice {invoice.Number} for {Money(invoice.Amount, invoice.Currency)} is on your Billing page.", cancellationToken);
            }

            if (now > end + grace && company.Status == "Active")
            {
                s.Status = "past_due";
                company.Status = "PastDue";
                pastDue++;
                await TellOwnerAsync(company, "Your subscription is past due",
                    "Your paid period has ended and no payment has arrived. You can still see your data; pay the open invoice on the Billing page to make changes again.", cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (invoiced, pastDue, ended);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A company that has none yet gets one that matches what it has now (its plan; on trial or active).</summary>
    private async Task<Subscription> EnsureSubscriptionAsync(Tenant company, CancellationToken cancellationToken, bool track = false)
    {
        var existing = track
            ? await db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == company.Id, cancellationToken)
            : await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == company.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var subscription = new Subscription
        {
            TenantId = company.Id,
            PlanId = company.PlanId,
            Status = company.Status switch { "Active" => "active", "PastDue" => "past_due", "Cancelled" => "cancelled", _ => "trialing" },
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);
        if (!track)
        {
            db.Entry(subscription).State = EntityState.Detached;
        }

        return subscription;
    }

    private void Activate(Tenant company, Subscription subscription, Plan plan, string interval, DateTime start, DateTime end)
    {
        subscription.PlanId = plan.Id;
        subscription.Interval = interval;
        subscription.Status = "active";
        subscription.CurrentPeriodStart = start;
        subscription.CurrentPeriodEnd = end;
        subscription.PendingPlanId = null;
        subscription.PendingInterval = null;
        subscription.UpdatedAt = clock.UtcNow;

        company.PlanId = plan.Id;
        company.Status = "Active";
        company.TrialEndsAt = null;
    }

    /// <summary>A new period starts when the current paid one ends, or now when nothing is paid ahead.</summary>
    private (DateTime Start, DateTime End) NextPeriod(Subscription subscription, string interval)
    {
        var now = clock.UtcNow;
        var start = subscription.Status == "active" && subscription.CurrentPeriodEnd is { } end && end > now ? end : now;
        return (start, Advance(start, interval));
    }

    private static DateTime Advance(DateTime from, string interval) => interval == Yearly ? from.AddYears(1) : from.AddMonths(1);

    private async Task<BillingInvoice> NewInvoiceAsync(Guid tenantId, Plan plan, string interval, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var number = await numbers.NextPlatformAsync("billing-invoice", cancellationToken);
        var invoice = new BillingInvoice
        {
            TenantId = tenantId,
            Number = $"ML-{clock.UtcNow:yyyy}-{number:D5}",
            PlanId = plan.Id,
            Interval = interval,
            Amount = PriceOf(plan, interval),
            Currency = plan.Currency,
            Status = "open",
            PeriodStart = start,
            PeriodEnd = end,
            IssuedAt = clock.UtcNow,
            DueAt = clock.UtcNow.AddDays(7),
        };
        db.BillingInvoices.Add(invoice);
        return invoice;
    }

    public static decimal PriceOf(Plan plan, string interval) => interval == Yearly ? (plan.PriceYearly ?? plan.PriceMonthly * 10) : plan.PriceMonthly;

    private static string NormaliseInterval(string? interval) => string.Equals(interval, Yearly, StringComparison.OrdinalIgnoreCase) ? Yearly
        : string.Equals(interval, Monthly, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(interval) ? Monthly
        : throw new ValidationException("interval", "Choose monthly or yearly.");

    private static string Money(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private async Task<IReadOnlyList<BillingInvoiceDto>> InvoicesAsync(Guid tenantId, IReadOnlyList<Plan> plans, int take, CancellationToken cancellationToken)
        => (await db.BillingInvoices.AsNoTracking().Where(i => i.TenantId == tenantId && i.Status != "void").OrderByDescending(i => i.IssuedAt).Take(take).ToListAsync(cancellationToken))
            .Select(i => Map(i, plans)).ToList();

    private static BillingInvoiceDto Map(BillingInvoice i, IReadOnlyList<Plan> plans)
        => new(i.Id, i.Number, plans.FirstOrDefault(p => p.Id == i.PlanId)?.Name ?? string.Empty, i.Interval, i.Amount, i.Currency, i.Status, i.PeriodStart, i.PeriodEnd, i.IssuedAt, i.DueAt, i.PaidAt, i.PaidVia, i.Reference, i.HostedUrl);

    private async Task<int> RecordsAsync(Guid tenantId, CancellationToken cancellationToken)
        => await db.Leads.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId && !x.IsDeleted, cancellationToken)
         + await db.Contacts.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId && !x.IsDeleted, cancellationToken)
         + await db.Accounts.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId && !x.IsDeleted, cancellationToken)
         + await db.Deals.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId && !x.IsDeleted, cancellationToken);

    private async Task<MindLift.Infrastructure.Identity.AppUser?> OwnerAsync(Guid tenantId, CancellationToken cancellationToken)
        => await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.TenantId == tenantId && u.IsTenantOwner).OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    private async Task<string> ActorAsync(CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } id)
        {
            return "Platform";
        }

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == id).Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync(cancellationToken);
        var name = user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim();
        return name.Length > 0 ? name : "Platform";
    }

    /// <summary>The company's owner hears about billing by email and in the app.</summary>
    private async Task TellOwnerAsync(Tenant company, string title, string message, CancellationToken cancellationToken)
    {
        var owner = await OwnerAsync(company.Id, cancellationToken);
        if (owner is null)
        {
            return;
        }

        try
        {
            if (owner.Email is { Length: > 0 } address)
            {
                await email.SendAsync(address, $"{title} - {company.Name}", $"Hello {owner.FirstName},\n\n{message}\n\nMindLift", cancellationToken);
            }

            using (tenant.Use(company.Id))
            {
                await notifications.NotifyAsync(new NotificationRequest([owner.Id], NotificationTypes.Billing, title, message, null, null, "/settings/billing", IncludeActor: true, InAppOnly: true), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Telling the owner must never undo the payment that was just recorded.
            logger.LogWarning(ex, "Could not tell the owner of {Company} about {Title}", company.Name, title);
        }
    }
}

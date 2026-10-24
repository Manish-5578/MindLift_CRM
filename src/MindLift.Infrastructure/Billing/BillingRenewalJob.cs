namespace MindLift.Infrastructure.Billing;

/// <summary>Daily: renewal invoices, past-due companies and subscriptions that end (see <see cref="BillingService.RunRenewalsAsync"/>).</summary>
public sealed class BillingRenewalJob(BillingService billing)
{
    public Task RunAsync(CancellationToken cancellationToken) => billing.RunRenewalsAsync(cancellationToken);
}

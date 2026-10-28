using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Common;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;


namespace MindLift.Infrastructure.Persistence;

/// <summary>
/// The application database context.
/// Every <see cref="TenantEntity"/> gets a global query filter on the current tenant (plan section 4.1.3).
/// With no tenant set, the filter matches nothing (fail closed).
/// </summary>
public class AppDbContext(DbContextOptions options, ITenantContext tenant)
    : IdentityUserContext<AppUser, Guid>(options), IAppDbContext
{
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<StoredFile> Files { get; set; } = null!;
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<PlatformAuditEntry> PlatformAudit { get; set; } = null!;
    public DbSet<NumberSequence> NumberSequences { get; set; } = null!;
    public DbSet<Subscription> Subscriptions { get; set; } = null!;
    public DbSet<BillingInvoice> BillingInvoices { get; set; } = null!;
    public DbSet<BillingEvent> BillingEvents { get; set; } = null!;
    public DbSet<EmailSuppression> EmailSuppressions { get; set; } = null!;
    public DbSet<SendingDomain> SendingDomains { get; set; } = null!;
    public DbSet<DataExport> DataExports { get; set; } = null!;
    public DbSet<SupportSession> SupportSessions { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<TicketComment> TicketComments { get; set; } = null!;
    public DbSet<Campaign> Campaigns { get; set; } = null!;
    public DbSet<CampaignRecipient> CampaignRecipients { get; set; } = null!;
    public DbSet<CalendarConnection> CalendarConnections { get; set; } = null!;
    public DbSet<CalendarEventLink> CalendarEventLinks { get; set; } = null!;
    public DbSet<ExternalBusyBlock> ExternalBusyBlocks { get; set; } = null!;
    public DbSet<CalendarFeed> CalendarFeeds { get; set; } = null!;
    public DbSet<Plan> Plans { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<TenantSettings> TenantSettings { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<ModuleSetting> ModuleSettings { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<Branch> Branches { get; set; } = null!;
    public DbSet<Invitation> Invitations { get; set; } = null!;
    public DbSet<PicklistValue> PicklistValues { get; set; } = null!;
    public DbSet<Lead> Leads { get; set; } = null!;
    public DbSet<Contact> Contacts { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<Deal> Deals { get; set; } = null!;
    public DbSet<DealContact> DealContacts { get; set; } = null!;
    public DbSet<DealStageHistory> DealStageHistory { get; set; } = null!;
    public DbSet<Activity> Activities { get; set; } = null!;
    public DbSet<ChecklistTemplate> ChecklistTemplates { get; set; } = null!;
    public DbSet<ActivityMention> ActivityMentions { get; set; } = null!;
    public DbSet<BookingPage> BookingPages { get; set; } = null!;
    public DbSet<DocumentTypeDefinition> DocumentTypeDefinitions { get; set; } = null!;
    public DbSet<Booking> Bookings { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<NotificationPreference> NotificationPreferences { get; set; } = null!;
    public DbSet<Pipeline> Pipelines { get; set; } = null!;
    public DbSet<PipelineStage> PipelineStages { get; set; } = null!;
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; set; } = null!;
    public DbSet<FieldLabelOverride> FieldLabelOverrides { get; set; } = null!;
    public DbSet<Layout> Layouts { get; set; } = null!;
    public DbSet<SavedView> SavedViews { get; set; } = null!;
    public DbSet<ExchangeRate> ExchangeRates { get; set; } = null!;
    public DbSet<AssignmentRule> AssignmentRules { get; set; } = null!;
    public DbSet<WebForm> WebForms { get; set; } = null!;
    public DbSet<AutomationRule> AutomationRules { get; set; } = null!;
    public DbSet<AutomationExecutionLog> AutomationExecutionLogs { get; set; } = null!;
    public DbSet<DashboardLayout> DashboardLayouts { get; set; } = null!;
    public DbSet<SalesTarget> SalesTargets { get; set; } = null!;
    public DbSet<ChannelAccount> ChannelAccounts { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<MessageTemplate> MessageTemplates { get; set; } = null!;
    public DbSet<CommunicationOptOut> CommunicationOptOuts { get; set; } = null!;
    public DbSet<ReportFolder> ReportFolders { get; set; } = null!;
    public DbSet<ReportDefinition> ReportDefinitions { get; set; } = null!;
    public DbSet<ReportFavorite> ReportFavorites { get; set; } = null!;
    public DbSet<ReportSchedule> ReportSchedules { get; set; } = null!;
    public DbSet<ReportExecutionLog> ReportExecutionLogs { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<PriceBook> PriceBooks { get; set; } = null!;
    public DbSet<PriceBookEntry> PriceBookEntries { get; set; } = null!;
    public DbSet<Quote> Quotes { get; set; } = null!;
    public DbSet<QuoteLineItem> QuoteLineItems { get; set; } = null!;
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceLineItem> InvoiceLineItems { get; set; } = null!;
    public DbSet<PaymentRecord> Payments { get; set; } = null!;
    public DbSet<BusinessDocument> BusinessDocuments { get; set; } = null!;
    public DbSet<PortalSettings> PortalSettings { get; set; } = null!;
    public DbSet<PortalUser> PortalUsers { get; set; } = null!;
    public DbSet<PortalToken> PortalTokens { get; set; } = null!;
    public DbSet<DocumentRequest> DocumentRequests { get; set; } = null!;
    public DbSet<TenantFeature> TenantFeatures { get; set; } = null!;
    public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; } = null!;
    public DbSet<IntegrationCredential> IntegrationCredentials { get; set; } = null!;
    public DbSet<TimelineEvent> TimelineEvents { get; set; } = null!;
    public DbSet<ContactAccountLink> ContactAccountLinks { get; set; } = null!;
    public DbSet<DealLineItem> DealLineItems { get; set; } = null!;
    public DbSet<DealStageGate> DealStageGates { get; set; } = null!;

    /// <summary>Tenant used by query filters. Guid.Empty when no tenant is set, so no rows match.</summary>
    protected Guid CurrentTenantId => tenant.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("pg_trgm");

        // Custom-field read functions used by filters (plan 4.6.4). Created by the M3 migration.
        builder.HasDbFunction(typeof(MindLift.Application.Common.Fields.CustomFieldFunctions).GetMethod(nameof(MindLift.Application.Common.Fields.CustomFieldFunctions.CfText))!);
        builder.HasDbFunction(typeof(MindLift.Application.Common.Fields.CustomFieldFunctions).GetMethod(nameof(MindLift.Application.Common.Fields.CustomFieldFunctions.CfNumeric))!);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Apply the tenant filter to every TenantEntity type discovered in the model.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                typeof(AppDbContext)
                    .GetMethod(nameof(ConfigureTenantEntity), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [builder]);
            }
        }

        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.HasQueryFilter(u => u.TenantId == CurrentTenantId);
            b.HasIndex(u => u.TenantId);
            // Emails are globally unique; this replaces the non-unique Identity index.
            b.HasIndex(u => u.NormalizedEmail).IsUnique();
            b.Property(u => u.FirstName).HasMaxLength(100);
            b.Property(u => u.LastName).HasMaxLength(100);
            b.Property(u => u.Language).HasMaxLength(5);
            b.Property(u => u.MustChangePassword).HasDefaultValue(false);
        });

        builder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(a => a.Id);
            b.HasQueryFilter(a => a.TenantId == CurrentTenantId);
            b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId, a.OccurredAt });
            b.Property(a => a.EntityType).HasMaxLength(100);
            b.Property(a => a.Action).HasMaxLength(30);
            b.Property(a => a.Changes).HasColumnType("jsonb");
        });

        // Global tables: not tenant-filtered.
        builder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasIndex(t => t.Slug).IsUnique();
            b.Property(t => t.Name).HasMaxLength(200);
            b.Property(t => t.Slug).HasMaxLength(100);
            b.Property(t => t.Status).HasMaxLength(20);
            b.Property(t => t.Country).HasMaxLength(2);
        });

        builder.Entity<Subscription>(b =>
        {
            b.ToTable("subscriptions");
            b.HasIndex(x => x.TenantId).IsUnique();
            b.HasIndex(x => x.ProviderSubscriptionId);
            b.Property(x => x.Interval).HasMaxLength(10);
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.Provider).HasMaxLength(30);
        });
        builder.Entity<BillingInvoice>(b =>
        {
            b.ToTable("billing_invoices");
            b.HasIndex(x => new { x.TenantId, x.IssuedAt });
            b.HasIndex(x => x.Number).IsUnique();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Status).HasMaxLength(10);
        });
        builder.Entity<BillingEvent>(b =>
        {
            b.ToTable("billing_events");
            b.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        });

        builder.Entity<EmailSuppression>(b =>
        {
            b.ToTable("email_suppressions");
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.Reason).HasMaxLength(20);
        });
        builder.Entity<SendingDomain>(b =>
        {
            b.ToTable("sending_domains");
            b.HasIndex(x => new { x.TenantId, x.Domain }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Domain).HasMaxLength(253);
        });

        builder.Entity<DataExport>(b =>
        {
            b.ToTable("data_exports");
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
            b.Property(x => x.Status).HasMaxLength(10);
        });

        builder.Entity<CalendarConnection>(b =>
        {
            b.ToTable("calendar_connections");
            b.HasIndex(x => new { x.TenantId, x.UserId, x.Provider });
            b.Property(x => x.Provider).HasMaxLength(20);
            b.Property(x => x.AccountEmail).HasMaxLength(320);
        });
        builder.Entity<CalendarEventLink>(b =>
        {
            b.ToTable("calendar_event_links");
            b.HasIndex(x => new { x.ConnectionId, x.ActivityId }).IsUnique();
            b.Property(x => x.ExternalId).HasMaxLength(1024);
            b.Property(x => x.ContentHash).HasMaxLength(64);
        });
        builder.Entity<ExternalBusyBlock>(b =>
        {
            b.ToTable("external_busy_blocks");
            b.HasIndex(x => new { x.TenantId, x.UserId, x.StartAt });
            b.HasIndex(x => x.ConnectionId);
            b.Property(x => x.ExternalId).HasMaxLength(1024);
        });
        builder.Entity<CalendarFeed>(b =>
        {
            b.ToTable("calendar_feeds");
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.UserId });
        });
        builder.Entity<Campaign>(b =>
        {
            b.ToTable("campaigns");
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            b.HasIndex(x => new { x.Status, x.ScheduledAt });
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Subject).HasMaxLength(200);
            b.Property(x => x.Status).HasMaxLength(10);
            b.Property(x => x.AudienceType).HasMaxLength(10);
        });
        builder.Entity<CampaignRecipient>(b =>
        {
            b.ToTable("campaign_recipients");
            b.HasIndex(x => new { x.TenantId, x.CampaignId, x.Status });
            b.HasIndex(x => x.Token).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Email });
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.Status).HasMaxLength(10);
            b.Property(x => x.Token).HasMaxLength(40);
        });
        builder.Entity<Ticket>(b =>
        {
            b.ToTable("tickets");
            b.HasIndex(x => new { x.TenantId, x.Status, x.LastActivityAt });
            b.HasIndex(x => new { x.TenantId, x.AssigneeId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Subject).HasMaxLength(200);
            b.Property(x => x.Status).HasMaxLength(10);
            b.Property(x => x.Priority).HasMaxLength(10);
            b.Property(x => x.Category).HasMaxLength(100);
            b.Property(x => x.Channel).HasMaxLength(10);
        });
        builder.Entity<TicketComment>(b =>
        {
            b.ToTable("ticket_comments");
            b.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt });
            b.Property(x => x.AuthorName).HasMaxLength(200);
        });

        builder.Entity<SupportSession>(b =>
        {
            b.ToTable("support_sessions");
            b.HasIndex(x => new { x.TenantId, x.StartedAt });
            b.HasIndex(x => x.CodeHash);
        });

        builder.Entity<NumberSequence>(b =>
        {
            b.ToTable("number_sequences");
            b.HasKey(x => new { x.TenantId, x.Key });
            b.Property(x => x.Key).HasMaxLength(40);
        });

        builder.Entity<Plan>(b =>
        {
            b.ToTable("plans");
            b.HasIndex(p => p.Key).IsUnique();
            b.Property(p => p.PriceMonthly).HasPrecision(18, 2);
            b.Property(p => p.PriceYearly).HasPrecision(18, 2);
            b.Property(p => p.Currency).HasMaxLength(3).HasDefaultValue("USD");
        });

        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasIndex(r => r.TokenHash).IsUnique();
            b.HasIndex(r => r.FamilyId);
            b.HasIndex(r => r.UserId);
        });

        // Tenant-owned configuration and records.
        builder.Entity<TenantSettings>(b =>
        {
            b.ToTable("tenant_settings");
            b.HasIndex(s => s.TenantId).IsUnique();
            b.Property(s => s.DefaultTaxRate).HasPrecision(5, 2);
        });

        builder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique().HasFilter("is_deleted = false");
            b.Property(r => r.Name).HasMaxLength(100);
            b.Property(r => r.DashboardView).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<RolePermission>(b =>
        {
            b.ToTable("role_permissions");
            b.HasIndex(p => new { p.TenantId, p.RoleId, p.Module }).IsUnique();
            b.Property(p => p.Module).HasConversion<string>().HasMaxLength(30);
            b.Property(p => p.Scope).HasConversion<string>().HasMaxLength(10);
        });

        builder.Entity<ModuleSetting>(b =>
        {
            b.ToTable("module_settings");
            b.HasIndex(m => new { m.TenantId, m.Module }).IsUnique();
            b.Property(m => m.Module).HasConversion<string>().HasMaxLength(30);
            b.Property(m => m.SingularLabel).HasMaxLength(100);
            b.Property(m => m.PluralLabel).HasMaxLength(100);
        });

        builder.Entity<Team>(b =>
        {
            b.ToTable("teams");
            b.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasFilter("is_deleted = false");
            b.Property(t => t.Name).HasMaxLength(100);
        });

        builder.Entity<Branch>(b =>
        {
            b.ToTable("branches");
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.TimeZoneId).HasMaxLength(64);
        });

        builder.Entity<Invitation>(b =>
        {
            b.ToTable("invitations");
            b.HasIndex(i => i.TokenHash).IsUnique();
            b.HasIndex(i => new { i.TenantId, i.NormalizedEmail });
            b.Property(i => i.Email).HasMaxLength(320);
            b.Property(i => i.NormalizedEmail).HasMaxLength(320);
            b.Property(i => i.FirstName).HasMaxLength(100);
            b.Property(i => i.LastName).HasMaxLength(100);
        });

        builder.Entity<PicklistValue>(b =>
        {
            b.ToTable("picklist_values");
            b.HasIndex(p => new { p.TenantId, p.PicklistKey, p.Value }).IsUnique().HasFilter("is_deleted = false");
            b.Property(p => p.PicklistKey).HasMaxLength(60);
            b.Property(p => p.Value).HasMaxLength(60);
            b.Property(p => p.Label).HasMaxLength(120);
            b.Property(p => p.LabelAr).HasMaxLength(120);
            b.Property(p => p.Category).HasMaxLength(40);
            b.Property(p => p.Color).HasMaxLength(9);
        });

        // Record tables use the registry keys as table names (see SystemFields), so SQL helpers can address them.
        builder.Entity<Lead>(b => { b.ToTable("leads"); b.HasIndex(x => new { x.TenantId, x.OwnerId, x.Status }); b.HasIndex(x => new { x.TenantId, x.NormalizedEmail }); b.HasIndex(x => new { x.TenantId, x.NormalizedPhone }); b.Property(x => x.FirstName).HasMaxLength(100); b.Property(x => x.LastName).HasMaxLength(100); b.Property(x => x.Tags).HasColumnType("text[]"); });
        builder.Entity<AssignmentRule>(b => { b.ToTable("assignment_rules"); b.HasIndex(x => new { x.TenantId, x.Priority }); b.Property(x => x.Conditions).HasColumnType("jsonb"); b.Property(x => x.UserIds).HasColumnType("jsonb"); });
        builder.Entity<WebForm>(b => { b.ToTable("web_forms"); b.HasIndex(x => x.PublicToken).IsUnique().HasFilter("is_deleted = false"); b.Property(x => x.Fields).HasColumnType("jsonb"); });
        builder.Entity<AutomationRule>(b =>
        {
            b.ToTable("automation_rules");
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.IsActive, x.Priority });
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.EntityType).HasMaxLength(40);
            b.Property(x => x.TriggerConfig).HasColumnType("jsonb");
            b.Property(x => x.Conditions).HasColumnType("jsonb");
            b.Property(x => x.Actions).HasColumnType("jsonb");
        });
        builder.Entity<AutomationExecutionLog>(b =>
        {
            b.ToTable("automation_execution_logs");
            b.HasIndex(x => new { x.TenantId, x.RuleId, x.ExecutedAt });
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            b.Property(x => x.RuleName).HasMaxLength(150);
            b.Property(x => x.EntityType).HasMaxLength(40);
            b.Property(x => x.TriggerType).HasMaxLength(40);
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        });
        builder.Entity<Contact>(b => { b.ToTable("contacts"); b.HasIndex(x => new { x.TenantId, x.AccountId }); b.HasIndex(x => new { x.TenantId, x.NormalizedEmail }); b.HasIndex(x => new { x.TenantId, x.NormalizedPhone }); b.Property(x => x.FirstName).HasMaxLength(100); b.Property(x => x.LastName).HasMaxLength(100); b.Property(x => x.JobTitle).HasMaxLength(150); });
        builder.Entity<Account>(b => { b.ToTable("accounts"); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Website).HasMaxLength(300); b.Property(x => x.Phone).HasMaxLength(50); b.Property(x => x.Industry).HasMaxLength(100); b.HasIndex(x => new { x.TenantId, x.ParentAccountId }); });
        builder.Entity<Deal>(b =>
        {
            b.ToTable("deals");
            b.HasIndex(x => new { x.TenantId, x.PipelineId, x.StageId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.AccountId });
            b.HasIndex(x => new { x.TenantId, x.PrimaryContactId });
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Name).HasMaxLength(200);
        });
        builder.Entity<DealContact>(b => { b.ToTable("deal_contacts"); b.HasIndex(x => new { x.TenantId, x.DealId }); b.HasIndex(x => new { x.DealId, x.ContactId }).IsUnique().HasFilter("is_deleted = false"); b.Property(x => x.Role).HasMaxLength(100); });
        builder.Entity<DealStageHistory>(b => { b.ToTable("deal_stage_history"); b.HasIndex(x => new { x.TenantId, x.DealId, x.ChangedAt }); });
        builder.Entity<ActivityMention>(b => { b.ToTable("activity_mentions"); b.HasIndex(x => new { x.TenantId, x.ActivityId }); b.HasIndex(x => new { x.ActivityId, x.UserId }).IsUnique().HasFilter("is_deleted = false"); });
        builder.Entity<DocumentTypeDefinition>(b => { b.ToTable("document_type_definitions"); b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique().HasFilter("is_deleted = false"); b.Property(x => x.Key).HasMaxLength(50); b.Property(x => x.Name).HasMaxLength(150); b.Property(x => x.KeywordsJson).HasColumnType("jsonb"); b.Property(x => x.FieldsJson).HasColumnType("jsonb"); });
        builder.Entity<BookingPage>(b => { b.ToTable("booking_pages"); b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false"); b.Property(x => x.Slug).HasMaxLength(60); b.Property(x => x.Name).HasMaxLength(150); b.Property(x => x.UserIds).HasColumnType("jsonb"); b.Property(x => x.Availability).HasColumnType("jsonb"); b.Property(x => x.Questions).HasColumnType("jsonb"); b.Property(x => x.ReminderOffsetsMinutes).HasColumnType("jsonb"); b.Property(x => x.LocationType).HasMaxLength(20); b.Property(x => x.AssignmentMode).HasMaxLength(20); b.Property(x => x.LocationValue).HasMaxLength(300); });
        builder.Entity<Booking>(b => { b.ToTable("bookings"); b.HasIndex(x => new { x.TenantId, x.UserId, x.StartAt }); b.HasIndex(x => x.ActivityId); b.HasIndex(x => x.ManageTokenHash).IsUnique(); b.Property(x => x.Answers).HasColumnType("jsonb"); b.Property(x => x.RemindersSent).HasColumnType("jsonb"); b.Property(x => x.Name).HasMaxLength(100); b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.Phone).HasMaxLength(40); b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.TimeZone).HasMaxLength(60); });
        builder.Entity<Notification>(b => { b.ToTable("notifications"); b.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead, x.CreatedAt }).IsDescending(false, false, false, true); b.HasIndex(x => new { x.UserId, x.GroupKey, x.EmailedAt }); b.Property(x => x.Type).HasMaxLength(40); b.Property(x => x.Title).HasMaxLength(250); b.Property(x => x.Body).HasMaxLength(2000); b.Property(x => x.Url).HasMaxLength(300); b.Property(x => x.GroupKey).HasMaxLength(200); b.Property(x => x.EntityType).HasMaxLength(40); });
        builder.Entity<NotificationPreference>(b => { b.ToTable("notification_preferences"); b.HasIndex(x => new { x.UserId, x.Type }).IsUnique().HasFilter("is_deleted = false"); b.Property(x => x.Type).HasMaxLength(40); });
        builder.Entity<ChecklistTemplate>(b => { b.ToTable("checklist_templates"); b.HasIndex(x => new { x.TenantId, x.Trigger }); b.Property(x => x.Name).HasMaxLength(150); b.Property(x => x.Items).HasColumnType("jsonb"); });
        builder.Entity<Activity>(b => { b.ToTable("activities"); b.HasIndex(x => new { x.TenantId, x.DealId }); b.HasIndex(x => new { x.TenantId, x.ContactId }); b.HasIndex(x => new { x.TenantId, x.AccountId }); b.HasIndex(x => new { x.TenantId, x.LeadId }); b.Property(x => x.Subject).HasMaxLength(250); b.Property(x => x.Type).HasMaxLength(20); b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.Priority).HasMaxLength(10); b.Property(x => x.Attendees).HasColumnType("jsonb"); b.HasIndex(x => new { x.TenantId, x.OwnerId, x.Status, x.DueAt }); });

        builder.Entity<Pipeline>(b => { b.ToTable("pipelines"); b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("is_deleted = false"); });
        builder.Entity<PipelineStage>(b => { b.ToTable("pipeline_stages"); b.HasIndex(x => new { x.TenantId, x.PipelineId, x.SortOrder }); });
        builder.Entity<CustomFieldDefinition>(b =>
        {
            b.ToTable("custom_field_definitions");
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.Key }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Key).HasMaxLength(50);
            b.Property(x => x.EntityType).HasMaxLength(30);
            b.Property(x => x.FieldType).HasMaxLength(20);
        });
        builder.Entity<FieldLabelOverride>(b => { b.ToTable("field_label_overrides"); b.HasIndex(x => new { x.TenantId, x.EntityType, x.FieldKey }).IsUnique(); });
        builder.Entity<Layout>(b =>
        {
            b.ToTable("layouts");
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.Kind }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Definition).HasColumnType("jsonb");
        });
        builder.Entity<SavedView>(b => { b.ToTable("saved_views"); b.HasIndex(x => new { x.TenantId, x.EntityType }); b.Property(x => x.Filter).HasColumnType("jsonb"); });
        builder.Entity<ExchangeRate>(b => { b.ToTable("exchange_rates"); b.HasIndex(x => new { x.TenantId, x.Currency, x.EffectiveDate }); b.Property(x => x.RateToBase).HasPrecision(18, 8); });

        builder.Entity<StoredFile>(b =>
        {
            b.ToTable("files");
            b.HasIndex(f => new { f.TenantId, f.EntityType, f.EntityId });
            b.Property(f => f.FileName).HasMaxLength(255);
            b.Property(f => f.ContentType).HasMaxLength(100);
            b.Property(f => f.StoragePath).HasMaxLength(500);
            b.Property(f => f.Sha256).HasMaxLength(64);
            b.Property(f => f.EntityType).HasMaxLength(100);
        });

        builder.Entity<DashboardLayout>(b =>
        {
            b.ToTable("dashboard_layouts");
            b.HasIndex(x => new { x.TenantId, x.UserId, x.View }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.View).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Layout).HasColumnType("jsonb");
        });

        builder.Entity<SalesTarget>(b =>
        {
            b.ToTable("sales_targets");
            b.HasIndex(x => new { x.TenantId, x.Metric, x.StartDate, x.EndDate });
            b.HasIndex(x => new { x.TenantId, x.UserId });
            b.HasIndex(x => new { x.TenantId, x.TeamId });
            b.Property(x => x.Metric).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.PeriodType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.TargetValue).HasPrecision(18, 2);
        });

        builder.Entity<ChannelAccount>(b =>
        {
            b.ToTable("channel_accounts");
            b.HasIndex(x => new { x.TenantId, x.Channel });
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.SenderAddress).HasMaxLength(255);
            b.Property(x => x.Host).HasMaxLength(255);
            b.Property(x => x.Username).HasMaxLength(255);
            b.Property(x => x.PhoneNumberId).HasMaxLength(100);
            b.Property(x => x.AccountSid).HasMaxLength(100);
            b.Property(x => x.StatusMessage).HasMaxLength(500);
        });

        builder.Entity<Message>(b =>
        {
            b.ToTable("messages");
            b.HasIndex(x => new { x.TenantId, x.ThreadId, x.CreatedAt });
            b.HasIndex(x => new { x.TenantId, x.Channel, x.CreatedAt });
            b.HasIndex(x => new { x.TenantId, x.LeadId });
            b.HasIndex(x => new { x.TenantId, x.ContactId });
            b.HasIndex(x => new { x.TenantId, x.AccountId });
            b.HasIndex(x => new { x.TenantId, x.DealId });
            b.HasIndex(x => new { x.TenantId, x.ExternalMessageId });
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.From).HasMaxLength(255);
            b.Property(x => x.To).HasMaxLength(255);
            b.Property(x => x.Subject).HasMaxLength(255);
            b.Property(x => x.ExternalMessageId).HasMaxLength(255);
            b.Property(x => x.AttachmentsJson).HasColumnType("jsonb");
        });

        builder.Entity<MessageTemplate>(b =>
        {
            b.ToTable("message_templates");
            b.HasIndex(x => new { x.TenantId, x.Channel, x.Code }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.Code).HasMaxLength(100);
            b.Property(x => x.Subject).HasMaxLength(255);
            b.Property(x => x.ExternalTemplateId).HasMaxLength(100);
        });

        builder.Entity<CommunicationOptOut>(b =>
        {
            b.ToTable("communication_opt_outs");
            b.HasIndex(x => new { x.TenantId, x.Target, x.Channel }).IsUnique().HasFilter("is_deleted = false");
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Target).HasMaxLength(255);
            b.Property(x => x.Reason).HasMaxLength(255);
        });

        builder.Entity<ReportFolder>(b =>
        {
            b.ToTable("report_folders");
            b.Property(x => x.Name).HasMaxLength(100);
            b.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReportDefinition>(b =>
        {
            b.ToTable("report_definitions");
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Source).HasMaxLength(50);
            b.Property(x => x.SystemKey).HasMaxLength(50);
            b.Property(x => x.ReportType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.QueryJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.SystemKey }).HasFilter("system_key IS NOT NULL");
            b.HasOne(x => x.Folder)
                .WithMany(x => x.Reports)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ReportFavorite>(b =>
        {
            b.ToTable("report_favorites");
            b.HasIndex(x => new { x.TenantId, x.ReportId, x.UserId }).IsUnique().HasFilter("is_deleted = false");
            b.HasOne(x => x.Report)
                .WithMany(x => x.Favorites)
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReportSchedule>(b =>
        {
            b.ToTable("report_schedules");
            b.Property(x => x.CronExpression).HasMaxLength(50);
            b.Property(x => x.Format).HasMaxLength(20);
            b.Property(x => x.RecipientsJson).HasColumnType("jsonb");
            b.HasOne(x => x.Report)
                .WithMany(x => x.Schedules)
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReportExecutionLog>(b =>
        {
            b.ToTable("report_execution_logs");
            b.Property(x => x.ReportName).HasMaxLength(150);
            b.Property(x => x.Source).HasMaxLength(50);
            b.Property(x => x.ExportFormat).HasMaxLength(20);
        });

        builder.Entity<Product>(b =>
        {
            b.ToTable("products");
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Sku).HasMaxLength(100);
            b.Property(x => x.Category).HasMaxLength(100);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.HasIndex(x => new { x.TenantId, x.Sku }).HasFilter("is_deleted = false");
        });

        builder.Entity<PriceBook>(b =>
        {
            b.ToTable("price_books");
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Currency).HasMaxLength(3);
        });

        builder.Entity<PriceBookEntry>(b =>
        {
            b.ToTable("price_book_entries");
            b.HasOne(x => x.PriceBook)
                .WithMany(x => x.Entries)
                .HasForeignKey(x => x.PriceBookId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PriceBookId, x.ProductId });
        });

        builder.Entity<Quote>(b =>
        {
            b.ToTable("quotes");
            b.Property(x => x.QuoteNumber).HasMaxLength(50);
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.HasIndex(x => new { x.TenantId, x.QuoteNumber });
            b.HasOne(x => x.Deal)
                .WithMany()
                .HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Contact)
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<QuoteLineItem>(b =>
        {
            b.ToTable("quote_line_items");
            b.Property(x => x.ProductName).HasMaxLength(200);
            b.HasOne(x => x.Quote)
                .WithMany(x => x.LineItems)
                .HasForeignKey(x => x.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Invoice>(b =>
        {
            b.ToTable("invoices");
            b.Property(x => x.InvoiceNumber).HasMaxLength(50);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.PaymentTerms).HasMaxLength(100);
            b.HasIndex(x => new { x.TenantId, x.InvoiceNumber });
            b.HasOne(x => x.Quote)
                .WithMany()
                .HasForeignKey(x => x.QuoteId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Deal)
                .WithMany()
                .HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Contact)
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InvoiceLineItem>(b =>
        {
            b.ToTable("invoice_line_items");
            b.Property(x => x.ProductName).HasMaxLength(200);
            b.HasOne(x => x.Invoice)
                .WithMany(x => x.LineItems)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PaymentRecord>(b =>
        {
            b.ToTable("payments");
            b.Property(x => x.ReferenceNumber).HasMaxLength(100);
            b.HasOne(x => x.Invoice)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BusinessDocument>(b =>
        {
            b.ToTable("business_documents");
            b.Property(x => x.FileName).HasMaxLength(255);
            b.Property(x => x.ContentType).HasMaxLength(100);
            b.Property(x => x.Category).HasMaxLength(50);
            b.Property(x => x.DetectedVendor).HasMaxLength(150);
            b.Property(x => x.DetectedInvoiceNumber).HasMaxLength(100);
            b.Property(x => x.OcrStructuredDataJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.Category });
            b.HasIndex(x => new { x.TenantId, x.AssociatedContactId });
            b.HasIndex(x => new { x.TenantId, x.AssociatedAccountId });
        });

        builder.Entity<PortalUser>(b =>
        {
            b.Property(x => x.SessionVersion).HasDefaultValue(1);
        });

        builder.Entity<TenantFeature>(b =>
        {
            b.ToTable("tenant_features");
            b.Property(x => x.Key).HasMaxLength(64);
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique().HasFilter("is_deleted = false");
        });

        builder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_records");
            b.Property(x => x.Scope).HasMaxLength(64);
            b.Property(x => x.Key).HasMaxLength(200);
            // The database decides who got there first: this index is the whole guarantee.
            b.HasIndex(x => new { x.TenantId, x.Scope, x.Key }).IsUnique();
            b.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<IntegrationCredential>(b =>
        {
            b.ToTable("integration_credentials");
            b.Property(x => x.Provider).HasMaxLength(64);
            b.Property(x => x.Label).HasMaxLength(100);
            b.Property(x => x.SettingsJson).HasMaxLength(4000);
            b.HasIndex(x => new { x.TenantId, x.Provider, x.Label }).IsUnique().HasFilter("is_deleted = false");
        });

        builder.Entity<TimelineEvent>(b =>
        {
            b.ToTable("timeline_events");
            b.Property(x => x.EntityType).HasMaxLength(48);
            b.Property(x => x.Kind).HasMaxLength(48);
            b.Property(x => x.Source).HasMaxLength(48);
            b.Property(x => x.Summary).HasMaxLength(300);
            b.Property(x => x.DetailJson).HasMaxLength(8000);
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.OccurredAt });
        });

        builder.Entity<ContactAccountLink>(b =>
        {
            b.ToTable("contact_account_links");
            b.Property(x => x.Role).HasMaxLength(100);
            b.HasIndex(x => new { x.TenantId, x.ContactId, x.AccountId }).IsUnique().HasFilter("is_deleted = false");
            b.HasIndex(x => new { x.TenantId, x.AccountId });
        });

        builder.Entity<DealLineItem>(b =>
        {
            b.ToTable("deal_line_items");
            b.Property(x => x.ProductName).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.Quantity).HasPrecision(18, 4);
            b.Property(x => x.UnitPrice).HasPrecision(18, 4);
            b.Property(x => x.DiscountPercent).HasPrecision(18, 4);
            b.Property(x => x.DiscountAmount).HasPrecision(18, 4);
            b.Property(x => x.TaxRate).HasPrecision(18, 4);
            b.Property(x => x.SubTotal).HasPrecision(18, 4);
            b.Property(x => x.TotalAmount).HasPrecision(18, 4);

            b.HasOne(x => x.Deal)
                .WithMany(x => x.LineItems)
                .HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(x => x.PriceBook)
                .WithMany()
                .HasForeignKey(x => x.PriceBookId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(x => new { x.TenantId, x.DealId });
        });

        builder.Entity<DealStageGate>(b =>
        {
            b.ToTable("deal_stage_gates");
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.MinAmount).HasPrecision(18, 4);
            b.Property(x => x.FailureMessage).HasMaxLength(500);
            b.Property(x => x.ConditionJson).HasMaxLength(8000);
            b.HasIndex(x => new { x.TenantId, x.PipelineId, x.StageId });
        });
    }

    private void ConfigureTenantEntity<TEntity>(ModelBuilder builder) where TEntity : TenantEntity
    {
        builder.Entity<TEntity>(b =>
        {
            b.HasQueryFilter(e => e.TenantId == CurrentTenantId && !e.IsDeleted);
            b.HasIndex(e => e.TenantId);

            // Optimistic concurrency: UPDATE ... WHERE version = @original (plan 4.1.1, see TenantEntity.Version).
            b.Property(e => e.Version).IsConcurrencyToken();
        });
    }
}



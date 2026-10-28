using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Administration;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Files;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Messaging;
using MindLift.Infrastructure.Onboarding;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Persistence.Interceptors;
using MindLift.Infrastructure.Security;
using MindLift.Infrastructure.Settings;
using MindLift.Infrastructure.Setup;
using MindLift.Infrastructure.Tenancy;
using MindLift.Infrastructure.Time;

namespace MindLift.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the database, identity, security, tenancy, administration services, mail, file storage and cache.</summary>
    public static IServiceCollection AddMindLiftInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.Configure<AppOptions>(configuration.GetSection("App"));

        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<MindLift.Infrastructure.Caching.CacheInvalidationInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>(), sp.GetRequiredService<MindLift.Infrastructure.Caching.CacheInvalidationInterceptor>()));
        services.AddScoped<MindLift.Infrastructure.Caching.RequestStateCache>();
        services.AddScoped<AuditRetentionJob>();
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddTokenProvider<DataProtectorTokenProvider<AppUser>>(TokenOptions.DefaultProvider)
            .AddTokenProvider<EmailTokenProvider<AppUser>>(TokenOptions.DefaultEmailProvider)
            .AddTokenProvider<PhoneNumberTokenProvider<AppUser>>(TokenOptions.DefaultPhoneProvider);

        // Authentication and tokens.
        services.AddSingleton<TokenService>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TenantSignupService>();
        services.AddScoped<NumberSequences>();

        // Billing. Manual (bank transfer, recorded by the platform owner) until a payment gateway is connected.
        services.AddSingleton<MindLift.Infrastructure.Billing.IPaymentProvider, MindLift.Infrastructure.Billing.ManualPaymentProvider>();
        services.AddScoped<MindLift.Infrastructure.Billing.BillingService>();
        services.AddScoped<MindLift.Infrastructure.Billing.BillingRenewalJob>();
        services.AddScoped<MindLift.Infrastructure.DataPortability.CompanyDataService>();
        services.AddScoped<MindLift.Infrastructure.Platform.SupportAccessService>();
        services.AddScoped<MindLift.Infrastructure.Tickets.TicketService>();
        services.AddScoped<MindLift.Infrastructure.Campaigns.CampaignService>();
        services.AddScoped<MindLift.Infrastructure.Campaigns.CampaignSendJob>();
        services.AddScoped<MindLift.Infrastructure.Campaigns.CampaignScheduleJob>();

        // Calendar sync: Google and Microsoft. A service stays off until its client id and secret are in the settings.
        services.AddHttpClient(MindLift.Infrastructure.CalendarSync.CalendarHttp.ClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<MindLift.Infrastructure.CalendarSync.ICalendarProvider, MindLift.Infrastructure.CalendarSync.GoogleCalendarProvider>();
        services.AddScoped<MindLift.Infrastructure.CalendarSync.ICalendarProvider, MindLift.Infrastructure.CalendarSync.MicrosoftCalendarProvider>();
        services.AddScoped<MindLift.Infrastructure.CalendarSync.CalendarSyncService>();
        services.AddScoped<MindLift.Infrastructure.CalendarSync.CalendarSyncJob>();
        services.AddScoped<MindLift.Infrastructure.Common.BulkQueue>();
        services.AddScoped<MindLift.Infrastructure.Common.BulkJob>();
        services.AddScoped<SystemSeedService>();
        services.AddScoped<PlatformAdminSeeder>();

        // Security.
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IDataScopeService, DataScopeService>();

        // Administration.
        services.AddScoped<IPlanLimitService, PlanLimitService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<RoleAdministrationService>();
        services.AddScoped<TeamBranchService>();
        services.AddScoped<AuditLogQuery>();
        services.AddScoped<PlatformService>();
        services.AddScoped<MindLift.Application.Abstractions.IPlatformEntitlements, MindLift.Infrastructure.Platform.PlatformEntitlements>();
        services.AddScoped<MindLift.Infrastructure.Platform.PlatformConsole>();
        services.AddScoped<MindLift.Application.Abstractions.IFeatureFlags, MindLift.Infrastructure.Platform.FeatureFlags>();
        services.AddScoped<MindLift.Application.Abstractions.IIdempotencyStore, MindLift.Infrastructure.Platform.IdempotencyStore>();
        services.AddScoped<MindLift.Application.Abstractions.ICredentialVault, MindLift.Infrastructure.Platform.CredentialVault>();
        services.AddScoped<MindLift.Application.Abstractions.ITimelineEventBus, MindLift.Infrastructure.Platform.TimelineEventBus>();
        services.AddScoped<MindLift.Application.Abstractions.IWebhookIngress, MindLift.Infrastructure.Platform.WebhookIngress>();
        services.AddScoped<MindLift.Application.Abstractions.IRecycleBinService, MindLift.Infrastructure.Platform.RecycleBinService>();
        services.AddScoped<MindLift.Infrastructure.Platform.RecycleBinService>();
        services.AddScoped<MindLift.Infrastructure.Platform.IdempotencyCleanupJob>();

        // Settings and configuration (M3).
        services.AddScoped<SettingsVersion>();
        services.AddScoped<CompanySettingsService>();
        services.AddScoped<FieldSettingsService>();
        services.AddScoped<PicklistPipelineService>();
        services.AddScoped<MindLift.Application.Abstractions.IDependentPicklistService, MindLift.Infrastructure.Settings.DependentPicklistService>();
        services.AddScoped<MindLift.Infrastructure.Settings.DependentPicklistService>();
        services.AddScoped<MindLift.Application.Abstractions.ILayoutRuleService, MindLift.Infrastructure.Settings.LayoutRuleService>();
        services.AddScoped<MindLift.Infrastructure.Settings.LayoutRuleService>();
        services.AddScoped<MindLift.Application.Abstractions.ICustomObjectService, MindLift.Infrastructure.Settings.CustomObjectService>();
        services.AddScoped<MindLift.Infrastructure.Settings.CustomObjectService>();
        services.AddScoped<MindLift.Application.Abstractions.IVisualWorkflowService, MindLift.Infrastructure.Settings.VisualWorkflowService>();
        services.AddScoped<MindLift.Infrastructure.Settings.VisualWorkflowService>();
        services.AddScoped<MindLift.Application.Abstractions.IBackupService, MindLift.Infrastructure.Settings.BackupService>();
        services.AddScoped<MindLift.Infrastructure.Settings.BackupService>();
        services.AddScoped<SavedViewService>();
        services.AddScoped<BootstrapService>();

        // Onboarding, templates, sample data and trial (M4).
        services.AddScoped<SampleDataService>();
        services.AddScoped<IndustryTemplateService>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<MindLift.Infrastructure.Trial.TrialExpiryJob>();
        services.AddScoped<MindLift.Infrastructure.Auth.RefreshTokenCleanupJob>();

        // Leads (M6).
        services.AddScoped<MindLift.Infrastructure.Leads.AssignmentService>();
        services.AddScoped<MindLift.Infrastructure.Leads.LeadService>();
        services.AddScoped<MindLift.Infrastructure.Leads.LeadSettingsService>();
        services.AddScoped<MindLift.Infrastructure.Leads.PublicFormService>();

        // Contacts and accounts (M7).
        services.AddScoped<MindLift.Infrastructure.Contacts.ContactService>();
        services.AddScoped<MindLift.Infrastructure.Contacts.ContactAccountLinkService>();
        services.AddScoped<MindLift.Infrastructure.Contacts.AccountService>();
        services.AddScoped<MindLift.Infrastructure.Contacts.GlobalSearchService>();

        // Deals (M8).
        services.AddScoped<MindLift.Infrastructure.Deals.DealService>();
        services.AddScoped<MindLift.Infrastructure.Deals.DealLineItemService>();
        services.AddScoped<MindLift.Infrastructure.Deals.DealStageGateService>();
        services.AddScoped<MindLift.Infrastructure.Activities.ActivityService>();
        services.AddScoped<MindLift.Infrastructure.Activities.QuickLogService>();
        services.AddScoped<MindLift.Infrastructure.Activities.ChecklistService>();
        services.AddScoped<MindLift.Infrastructure.Activities.TimelineService>();

        // Notifications (M10).
        services.TryAddSingleton<INotificationPusher, NullNotificationPusher>();
        services.AddScoped<INotificationService, MindLift.Infrastructure.Notifications.NotificationService>();
        services.AddScoped<MindLift.Infrastructure.Notifications.NotificationQueryService>();
        services.AddScoped<MindLift.Infrastructure.Notifications.NotificationEmailJob>();
        services.AddScoped<MindLift.Infrastructure.Portal.PortalEmailJob>();
        services.AddScoped<MindLift.Infrastructure.Notifications.ImportNotifier>();
        services.AddScoped<MindLift.Infrastructure.Notifications.ActivityReminderJob>();
        services.AddScoped<MindLift.Infrastructure.Notifications.OverdueTaskJob>();
        services.AddScoped<MindLift.Infrastructure.Notifications.DailyDigestJob>();
        services.AddScoped<MindLift.Infrastructure.Notifications.NotificationCleanupJob>();

        // Calendar and booking pages (M11).
        services.AddScoped<MindLift.Infrastructure.Bookings.AvailabilityService>();
        services.AddScoped<MindLift.Infrastructure.Bookings.BookingPageService>();
        services.AddScoped<MindLift.Infrastructure.Bookings.PublicBookingService>();
        services.AddScoped<MindLift.Infrastructure.Bookings.CalendarService>();
        services.AddScoped<MindLift.Infrastructure.Bookings.BookingReminderJob>();
        services.AddScoped<MindLift.Infrastructure.Deals.IDealWonHandler>(sp => sp.GetRequiredService<MindLift.Infrastructure.Activities.ChecklistService>());

        // Mail.
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<MindLift.Infrastructure.Messaging.EmailGuard>();
        services.AddScoped<MindLift.Infrastructure.Messaging.SendingDomainService>();
        services.AddSingleton<MindLift.Infrastructure.Messaging.IDnsTxtLookup, MindLift.Infrastructure.Messaging.DnsTxtLookup>();

        // In-memory cache, shared through Redis when Redis:ConnectionString is set (needed when more than one server runs).
        var redis = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "mindlift:";
            });
        }

        services.AddHybridCache();

        // Files go to the server's disk (Local) or to a bucket (S3: Amazon S3, Cloudflare R2, MinIO and the like).
        if (string.Equals(configuration["Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
        {
            var s3 = configuration.GetSection("Storage:S3").Get<S3StorageOptions>() ?? new S3StorageOptions();
            services.AddSingleton<IFileStorage>(new S3FileStorage(s3));
        }
        else
        {
            var storagePath = configuration["Storage:LocalPath"] ?? "App_Data/files";
            services.AddSingleton<IFileStorage>(new LocalFileStorage(storagePath));
        }
        services.AddScoped<FileService>();

        // Automation & Save Pipeline (Core Zoho TCA Engine).
        services.AddHttpClient();
        // Automation webhooks go out through a handler that checks the address it actually connects to, and never follows a redirect to somewhere else.
        services.AddHttpClient("AutomationWebhooks").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ConnectCallback = MindLift.Infrastructure.Automation.WebhookSecurity.ConnectSafelyAsync,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        });
        services.AddScoped<MindLift.Application.Common.Automation.IAutomationExecutionContext, MindLift.Application.Common.Automation.AutomationExecutionContext>();
        services.AddScoped<MindLift.Application.Abstractions.IAutomationEngine, MindLift.Infrastructure.Automation.AutomationEngine>();
        services.AddScoped<MindLift.Infrastructure.Automation.AutomationOutboxJob>();
        services.AddScoped<MindLift.Application.Abstractions.IRecordSavePipeline, MindLift.Infrastructure.Automation.RecordSavePipeline>();
        services.AddScoped<MindLift.Infrastructure.Automation.AutomationScheduledJob>();

        // Dashboard & Analytics (M13).
        services.AddScoped<ISalesMetrics, MindLift.Infrastructure.Dashboard.SalesMetrics>();
        services.AddScoped<IDashboardService, MindLift.Infrastructure.Dashboard.DashboardService>();
        services.AddScoped<ISalesTargetService, MindLift.Infrastructure.Dashboard.SalesTargetService>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.KpiStripWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.MyDayWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.UpcomingMeetingsWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.NewLeadsWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.DealsInProgressWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.DealsClosingSoonWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.TargetProgressWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.SalesTrendWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.LeadsBySourceWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.PipelineFunnelWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.TeamLeaderboardWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.TeamOverdueWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.RecentWinsWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.ActivitySummaryWidget>();
        services.AddScoped<IDashboardWidget, MindLift.Infrastructure.Dashboard.SystemOverviewWidget>();

        // Omnichannel Communication & Unified Inbox (M12).
        services.AddSingleton<MindLift.Infrastructure.Communication.ISecretProtector, MindLift.Infrastructure.Communication.SecretProtector>();
        services.AddScoped<IChannelSender, MindLift.Infrastructure.Communication.EmailChannelSender>();
        services.AddScoped<IChannelSender, MindLift.Infrastructure.Communication.WhatsAppChannelSender>();
        services.AddScoped<IChannelSender, MindLift.Infrastructure.Communication.TwilioSmsSender>();
        services.AddScoped<IOptOutService, MindLift.Infrastructure.Communication.OptOutService>();
        services.AddScoped<IMessageTemplateService, MindLift.Infrastructure.Communication.MessageTemplateService>();
        services.AddScoped<IChannelAccountService, MindLift.Infrastructure.Communication.ChannelAccountService>();
        services.AddScoped<ICommunicationService, MindLift.Infrastructure.Communication.CommunicationService>();
        services.AddScoped<MindLift.Infrastructure.Communication.WebhookAuthenticator>();
        services.AddScoped<MindLift.Infrastructure.Tenancy.TenantStanding>();

        // Reports & Analytics (M14).
        services.AddSingleton<MindLift.Infrastructure.Reporting.IReportDataSourceRegistry, MindLift.Infrastructure.Reporting.ReportDataSourceRegistry>();
        services.AddScoped<MindLift.Infrastructure.Reporting.ReportSqlBuilder>();
        services.AddScoped<MindLift.Infrastructure.Reporting.IReportExecutor, MindLift.Infrastructure.Reporting.DapperReportExecutor>();
        services.AddScoped<MindLift.Infrastructure.Reporting.ISystemReportRegistry, MindLift.Infrastructure.Reporting.SystemReportRegistry>();
        services.AddScoped<MindLift.Infrastructure.Reporting.IReportService, MindLift.Infrastructure.Reporting.ReportService>();
        services.AddScoped<MindLift.Infrastructure.Reporting.ReportScheduleJob>();

        // CPQ & Documents (M16).
        services.Configure<MindLift.Infrastructure.Cpq.OcrOptions>(configuration.GetSection("Ocr"));

        // AI reading of document fields: a local model (Ollama). Off for a workspace until its administrator switches it on.
        services.Configure<MindLift.Infrastructure.Documents.Ai.LlmOptions>(configuration.GetSection("Llm"));
        services.AddHttpClient(MindLift.Infrastructure.Documents.Ai.OllamaLlmClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<MindLift.Infrastructure.Documents.Ai.ILlmClient, MindLift.Infrastructure.Documents.Ai.OllamaLlmClient>();
        services.AddSingleton<MindLift.Infrastructure.Documents.Ai.AiFieldExtractor>();
        services.AddScoped<MindLift.Infrastructure.Documents.Extraction.DocumentFieldReader>();
        services.AddScoped<MindLift.Infrastructure.Documents.Extraction.DocumentRecordApplier>();
        services.AddScoped<MindLift.Infrastructure.Cpq.DocumentReadJob>();
        // A single IOcrEngine resolves to the LAST registration, so Tesseract (the primary reader) must be registered last.
        // The PaddleOCR engine is registered first: it is only asked for pages Tesseract still reads poorly.
        services.AddSingleton<MindLift.Infrastructure.Cpq.IOcrEngine, MindLift.Infrastructure.Cpq.RapidOcrEngine>();
        services.AddSingleton<MindLift.Infrastructure.Cpq.IOcrEngine, MindLift.Infrastructure.Cpq.TesseractCliOcrEngine>();
        services.AddScoped<ICpqService, MindLift.Infrastructure.Cpq.CpqService>();
        services.AddScoped<IDocumentOcrService, MindLift.Infrastructure.Cpq.DocumentOcrService>();
        services.AddSingleton<MindLift.Infrastructure.Documents.Text.OcrGate>();
        services.AddScoped<MindLift.Infrastructure.Documents.Text.DocumentPageReader>();
        services.AddScoped<MindLift.Infrastructure.Documents.Text.SearchablePdfService>();
        services.AddScoped<MindLift.Infrastructure.Documents.Extraction.DocumentTypeService>();
        services.AddScoped<MindLift.Infrastructure.Cpq.DocumentExpiryJob>();
        services.AddScoped<MindLift.Infrastructure.Cpq.OcrHealthCheck>();

        // Customer Portal (M17).
        services.AddScoped<IPortalService, MindLift.Infrastructure.Portal.PortalService>();

        return services;
    }
}

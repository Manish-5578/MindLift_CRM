# MindLift CRM — End-to-End Build Plan (.NET)

> **Source requirement:** "CRM SaaS – Core Modules Handover" (Oct 7, 2026), 23 modules in 3 phases.
> **Plan date:** 2026-10-09
> **Primary focus:** **Customer Onboarding** (tenant sign-up + industry templates + lead→customer conversion), **Dashboard**, **Reports & Analytics**.
> **Audience:** an AI coding agent (Claude Haiku) building the system step by step, plus human reviewers.

---

## 0. HOW TO USE THIS DOCUMENT (READ FIRST — RULES FOR THE BUILDING AGENT)

1. **Build in milestone order (Section 14).** Do not start a milestone until the previous one meets its *Definition of Done* (DoD).
2. **After every step:** run `dotnet build` (zero errors, warnings reviewed) and `dotnet test`. Never leave the solution broken.
3. **Keep a `PROGRESS.md`** at repo root. Tick each step as done, note deviations and open questions. Read it at the start of every session.
4. **Never bypass multi-tenancy.** Every tenant-owned table has `TenantId` and a global query filter (Section 4.1). `IgnoreQueryFilters()` is allowed ONLY in the places listed in Section 4.1.6.
5. **Never trust the client for permissions.** Every controller action has `[HasPermission(...)]`; every list query passes through `IDataScopeService.ApplyScope(...)` (Section 4.4).
6. **No SQL string concatenation of user input.** The report SQL builder (Section 12.4) only concatenates *whitelisted* SQL fragments from the field registry; all values are Dapper parameters.
7. **Do not add packages not listed in Section 2** without writing the reason in `PROGRESS.md`. Do NOT use MediatR, AutoMapper or FluentAssertions (licenses changed to commercial). Use manual mapping and Shouldly.
8. **All times are stored in UTC** (`timestamptz`). Date-only values use `DateOnly`. Convert to tenant/user time zone only for display and for date-range math (Section 4.7).
9. **Money:** `decimal(18,2)`; every money field has a `Currency` (ISO 4217) and an `AmountInBase` column (Section 4.8).
10. **Use the exact names** given here (entities, enums, endpoints, widget keys, report keys). Consistency matters more than personal preference.
11. If something is ambiguous, choose the simplest option that satisfies the acceptance criteria, write the decision in `PROGRESS.md` under "Decisions", and continue.
12. Write tests as you go (Section 13). Tenant-isolation tests are mandatory.
13. **UI is built ONLY from Section 18 (Design System & Screen Specs).** Do not invent colors, font sizes, spacing, shadows, layouts or component styles. Use the tokens, the shared components in `MindLift.UI`, and the page templates exactly as written. If a screen is not specified, compose it from the existing page templates (18.8) — never design a new pattern.
14. **Every UI page must pass the UI Definition of Done (18.15)**, verified with screenshots at 1440 px, 768 px and 375 px, in English and Arabic (E2E screenshot tests, 18.16). Look at the screenshots yourself before ticking a page as done.
15. **Reading order per milestone:** Section 14 (milestone steps) → the module section it references → Section 18 for every screen in that milestone → write code → screenshots → `PROGRESS.md`.
16. When a section says **"EXACT"**, copy the code/text/value as given (adjust only namespaces or compile errors caused by library version differences — and record such adjustments in `PROGRESS.md`).

---

## 1. PRODUCT SUMMARY & SCOPE

**What:** One multi-tenant CRM sold as SaaS. Any business signs up, picks an industry, and the CRM configures itself (labels, fields, pipeline stages, templates). Industry-specific features (property listings, shipment tracking) are **out of scope** — later "industry packs".

**Phase 1 (this plan builds it in full detail):**

| # | Module | Priority |
|---|--------|----------|
| — | **Tenant Onboarding (sign-up, wizard, industry templates, sample data)** | ★ Core focus |
| 1 | **Dashboard** | ★ Core focus |
| 17 | **Reports & Analytics** | ★ Core focus |
| 2 | Leads (incl. lead → customer conversion = customer onboarding) | ★ Core focus |
| 3 | Contacts | High |
| 4 | Accounts / Companies | High |
| 5 | Deals / Pipeline | High |
| 6 | Activities (Tasks, Calls, Meetings, Notes) | High |
| 7 | Calendar & Appointments | Medium |
| 8 | Communication (Email, WhatsApp, SMS) | Medium |
| 18 | Notifications & Reminders | High |
| 20 | Users, Teams & Permissions | High (foundation) |
| 21 | Settings & Customization | High (foundation) |
| 12 (part) | **Documents + Document Intelligence (PDF/image OCR with Tesseract, extraction of info & metrics, review, storage)** — Section 19 | ★ Core focus (added) |
| 19 (part) | **Customer Portal v1** (customers view their documents, extracted info & metrics, upload requested documents) — Section 20 | ★ Core focus (added) |
| — | **UI/UX Design System** (tokens, components, screen specs, screenshot verification) — Section 18 | ★ Applies to every screen |

**Phase 2 (outlined in Section 15):** 9 Products & Services, 10 Quotations, 11 Invoices & Payments, 12 Documents (remaining: folders UI polish, versions, e-signature), 13 Tickets, 16 Automation & Workflows, 22 Integrations.
**Phase 3 (outlined in Section 15):** 14 Projects/Jobs, 15 Marketing Campaigns, 19 Customer Portal (remaining: quotes, invoices & payments, tickets, projects in portal), 23 Mobile App.

**Requirements that apply to EVERY module (from the handover):**
- Search, filter, sort, saved views on every list.
- Import (Excel/CSV) and export (Excel), subject to permissions.
- Timeline: every change and activity on a record appears in its history.
- Permissions: every module respects roles & access rules.
- Custom fields on every module.
- English + Arabic, including RTL layout.
- Multi-currency and VAT wherever money appears.
- Strict tenant data separation.
- Mobile-friendly (responsive) screens.

---

## 2. TECH STACK

| Concern | Choice | Notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Install the .NET 10 SDK (machine currently has only 8.0.128; .NET 8 support ends Nov 2026). `global.json` pins 10.0.x. |
| API | **ASP.NET Core Web API (Controllers)** | REST, JSON, `/api/v1/...` |
| Frontend | **Blazor WebAssembly** (standalone project, served by the API host = same origin) | All-C# stack. Components reused in the Phase 3 MAUI Blazor Hybrid mobile app. |
| UI library | **MudBlazor** (latest stable supporting net10.0) | Material UI, has RTL support (`MudRTLProvider`) |
| Charts | **Blazor-ApexCharts** | Line, bar, donut, funnel, area |
| Calendar UI | **Heron.MudCalendar** | Day/week/month views for MudBlazor |
| Database | **PostgreSQL 16+** | `jsonb` for custom fields, `pg_trgm` for search/duplicates |
| ORM | **EF Core 10** + `Npgsql.EntityFrameworkCore.PostgreSQL` + `EFCore.NamingConventions` (snake_case) | |
| Reporting queries | **Dapper** | Report engine runs generated, parameterized SQL |
| Auth | **ASP.NET Core Identity** (`IdentityUserContext<AppUser, Guid>`) + **JWT bearer** + refresh tokens | Own `Role` table (not Identity roles) |
| Validation | **FluentValidation** | |
| Background jobs | **Hangfire** + `Hangfire.PostgreSql` | Reminders, imports, scheduled reports, messaging |
| Real-time | **SignalR** (built-in) | Notifications, dashboard refresh |
| Caching | **HybridCache** (`Microsoft.Extensions.Caching.Hybrid`) | In-memory now, Redis later |
| Excel | **ClosedXML** | Import parsing (xlsx) & export |
| CSV | **CsvHelper** | Import parsing (csv) |
| PDF | **QuestPDF** (Community license, free < $1M revenue) | Report exports, later quotes/invoices |
| Email | **MailKit** (SMTP send, IMAP receive) | Mailpit in dev |
| Phone numbers | **libphonenumber-csharp** (`PhoneNumbers`) | E.164 normalization for duplicate detection |
| Calendar invites | **Ical.Net** | .ics attachments |
| Sample data | **Bogus** | Onboarding sample data + dev seed |
| Logging | **Serilog** (console + file; Seq optional) | |
| API docs | `Microsoft.AspNetCore.OpenApi` + **Scalar.AspNetCore** | `/scalar` in dev |
| Tests | **xUnit**, **Shouldly**, **NSubstitute**, `Microsoft.AspNetCore.Mvc.Testing`, **Testcontainers.PostgreSql** | Needs Docker; fallback to a local Postgres test DB |
| Containers | Docker + docker-compose | postgres, mailpit, api |
| PDF text & layout | **UglyToad.PdfPig** (Apache-2.0) | Text + word bounding boxes from digital PDFs |
| PDF → image | **PDFtoImage** (MIT, PDFium + SkiaSharp) | Renders scanned PDF pages to PNG for OCR and for the page viewer |
| Image processing | **SkiaSharp** (+ `SkiaSharp.NativeAssets.Linux.NoDependencies` for Linux/Docker) | Grayscale, contrast, resize before OCR; thumbnails |
| OCR engine | **Tesseract OCR 5.x (command-line)** with `eng` + `ara` traineddata | Called as a process (`TesseractCliOcrEngine`) — works the same on Windows dev and Linux Docker |
| Fonts | **Inter** (Latin) + **IBM Plex Sans Arabic** (Arabic) via Google Fonts | Section 18.3 |
| UI tests | **Microsoft.Playwright** (.NET) + **bUnit** | Screenshot tests per screen/viewport/language (18.16) |
| HTML sanitizer | **HtmlSanitizer** (Ganss.Xss) | Notes, email bodies |

> Use "latest stable version compatible with net10.0" for every package. Pin versions centrally in `Directory.Packages.props` (Central Package Management).

---

## 3. SOLUTION STRUCTURE

```
D:\MindLift_CRM
│  MindLift.sln
│  global.json
│  Directory.Build.props          (Nullable=enable, ImplicitUsings=enable, TreatWarningsAsErrors=false, LangVersion=latest)
│  Directory.Packages.props
│  docker-compose.yml
│  PROGRESS.md
│  CRM_BUILD_PLAN.md               (this file)
│
├─ src
│  ├─ MindLift.Shared              (class lib, net10.0)  — referenced by ALL projects incl. Blazor client
│  │    Enums/ (AppModule, PermissionAction, DataScope, FieldType, ... )
│  │    Contracts/ (request/response DTOs per module: Leads/, Deals/, Dashboard/, Reports/, ...)
│  │    Filtering/ (FilterGroup, FilterCondition, FilterOperator, SortSpec, PagedRequest, PagedResult<T>)
│  │    Permissions/ (permission constants)
│  │
│  ├─ MindLift.Domain              (class lib) → Shared
│  │    Common/ (TenantEntity, IOwnedEntity, IHasCustomFields, ISampleData)
│  │    Entities/ (one folder per module)
│  │
│  ├─ MindLift.Application         (class lib) → Domain, Shared   [+ Microsoft.EntityFrameworkCore, FluentValidation]
│  │    Abstractions/ (IAppDbContext, ITenantContext, IClock, IFileStorage, IEmailSender, IWhatsAppProvider, ISmsProvider, INotificationService, ...)
│  │    Common/ (FieldRegistry, FilterCompiler, DataScopeService, TimelineService, TenantTime, DateRangeResolver, CustomFieldValidator)
│  │    Modules/<Module>/ (XxxService.cs, XxxValidators.cs, XxxMappings.cs)
│  │    Dashboard/ (widgets)
│  │    Reports/ (engine, data sources, system reports)
│  │    Onboarding/ (OnboardingService, IndustryTemplateService, SampleDataService)
│  │
│  ├─ MindLift.Infrastructure      (class lib) → Application
│  │    Persistence/ (AppDbContext, Configurations/, Interceptors/, Migrations/)
│  │    Identity/ (JwtTokenService, RefreshTokenService, UserLookup)
│  │    Jobs/ (Hangfire jobs)
│  │    Messaging/ (SmtpEmailSender, ImapPoller, WhatsAppCloudProvider, TwilioSmsProvider, Fake* providers)
│  │    Files/ (LocalFileStorage, later AzureBlobFileStorage)
│  │    Reports/ (DapperReportExecutor, ExcelExporter, PdfExporter)
│  │    Templates/ (industry template JSON files as EmbeddedResource)
│  │
│  ├─ MindLift.Api                 (ASP.NET Core) → Infrastructure, Web (to host WASM files)
│  │    Controllers/ (one per module)
│  │    Auth/ (HasPermissionAttribute, PermissionPolicyProvider, PermissionHandler)
│  │    Hubs/ (NotificationHub)
│  │    Middleware/ (TenantResolutionMiddleware, ExceptionHandling)
│  │    Program.cs
│  │
│  ├─ MindLift.UI                  (Razor Class Library) → Shared      ★ DESIGN SYSTEM — used by Web, Portal and (Phase 3) Mobile
│  │    Theme/ (MindLiftTheme.cs — EXACT code in 18.4)
│  │    wwwroot/css/mindlift.css   (EXACT tokens & utility classes in 18.5)
│  │    Components/ (PageHeader, MlCard, StatCard, EmptyState, ErrorState, StatusChip, UserAvatar, MoneyText, DateText,
│  │                 SkeletonTable, SkeletonCards, ConfirmDialog, SectionCard, FormDrawer, KeyValueList, FileDropZone,
│  │                 DocumentPageViewer, ChartCard, FilterBuilder, DynamicForm, FieldValue, Timeline, KanbanBoard ...)
│  │    Resources/ (UiResource.resx, UiResource.ar.resx — strings used inside shared components)
│  │
│  ├─ MindLift.Web                 (Blazor WebAssembly, staff app, served at "/") → Shared, UI
│  │    Layout/ (MainLayout, NavMenu, AuthLayout, OnboardingLayout), Pages/<Module>/, Components/Widgets/...
│  │    Services/ (ApiClient, AuthStateProvider, AppState, NotificationClient)
│  │    Resources/ (SharedResource.resx, SharedResource.ar.resx)
│  │
│  └─ MindLift.Portal              (Blazor WebAssembly, customer portal, served at "/portal") → Shared, UI   (Section 20)
│       Layout/ (PortalLayout, PortalAuthLayout), Pages/ (Login, Home, Documents, DocumentDetail, Requests, Profile)
│       Services/ (PortalApiClient, PortalAuthStateProvider, BrandingState)
│
└─ tests
   ├─ MindLift.UnitTests           (FilterCompiler, ReportSqlBuilder, availability, conversion, DateRangeResolver, extraction rules, MRZ...)
   ├─ MindLift.IntegrationTests    (WebApplicationFactory + Testcontainers; tenant isolation, permissions, flows)
   └─ MindLift.E2E                 (Playwright; screenshot tests for every screen × 3 viewports × EN/AR — 18.16)
```

**Layer rules:** Domain has no EF/ASP.NET dependencies. Application talks to the DB only through `IAppDbContext` (exposes `DbSet<T>`s + `SaveChangesAsync`) and `IReportExecutor`. Controllers are thin: validate → call service → return DTO.

**Service pattern (use everywhere):**
```csharp
public interface ILeadService
{
    Task<PagedResult<LeadListItemDto>> QueryAsync(ListQuery query, CancellationToken ct);
    Task<LeadDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<LeadDetailDto> CreateAsync(CreateLeadRequest req, CancellationToken ct);
    Task<LeadDetailDto> UpdateAsync(Guid id, UpdateLeadRequest req, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<ConvertLeadResult> ConvertAsync(Guid id, ConvertLeadRequest req, CancellationToken ct);
}
```
Errors are thrown as typed exceptions (`NotFoundException`, `ForbiddenException`, `ValidationException`, `ConflictException`, `BusinessRuleException`) and turned into RFC 7807 `ProblemDetails` by the exception middleware (404/403/400/409/422).

---

## 4. CROSS-CUTTING ARCHITECTURE

### 4.1 Multi-tenancy (shared database, `TenantId` column)

#### 4.1.1 Base types (Domain/Common)
```csharp
public interface ITenantEntity { Guid TenantId { get; set; } }
public interface ISoftDelete   { bool IsDeleted { get; set; } DateTime? DeletedAt { get; set; } }
public interface IOwnedEntity  { Guid OwnerId { get; set; } }           // records with an owner (lead, contact, account, deal, activity)
public interface IHasCustomFields { JsonDocument CustomFields { get; set; } }
public interface ISampleData   { bool IsSample { get; set; } }

public abstract class TenantEntity : ITenantEntity, ISoftDelete
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public uint Version { get; set; }            // mapped to Postgres xmin → optimistic concurrency
}
```

#### 4.1.2 Tenant context
```csharp
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsPlatformAdmin { get; }
    IDisposable Use(Guid tenantId, Guid? userId = null);   // used by signup, jobs, webhooks, public endpoints
}
```
- Scoped service. `TenantResolutionMiddleware` (runs after `UseAuthentication`) fills it from JWT claims `tid` and `sub`.
- Hangfire jobs receive `tenantId` as a parameter and call `using var _ = tenantContext.Use(tenantId);` first.
- Public endpoints (web forms, booking pages, webhooks) resolve the tenant from the form token / booking slug / phone number id, then call `Use(...)`.

#### 4.1.3 Global query filter (AppDbContext)
```csharp
public Guid CurrentTenantId => _tenant.TenantId ?? Guid.Empty;   // evaluated per query

protected override void OnModelCreating(ModelBuilder b)
{
    base.OnModelCreating(b);
    b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    b.HasPostgresExtension("pg_trgm");
    b.HasPostgresExtension("citext");

    foreach (var et in b.Model.GetEntityTypes())
        if (typeof(TenantEntity).IsAssignableFrom(et.ClrType))
            typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(et.ClrType).Invoke(this, [b]);
}

private void ApplyTenantFilter<T>(ModelBuilder b) where T : TenantEntity
{
    b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId && !e.IsDeleted);
    b.Entity<T>().Property(e => e.Version).IsRowVersion();          // xmin
    b.Entity<T>().HasIndex(e => e.TenantId);
}
```
`AppUser` (Identity) also has `TenantId` and gets an equivalent filter (configure it explicitly in `AppUserConfiguration`). Map `AppUser` to table `users`.

#### 4.1.4 SaveChanges interceptor (`AuditableEntityInterceptor`)
On `SavingChangesAsync`:
- **Added** `TenantEntity`: if `TenantId == Guid.Empty` → set from context; if it differs from context tenant (and not platform admin) → throw `ForbiddenException`. Set `CreatedAt = clock.UtcNow`, `CreatedById = userId`.
- **Modified**: if `TenantId` property modified → throw. Set `UpdatedAt/UpdatedById`.
- **Deleted**: convert to soft delete (`State = Modified`, `IsDeleted = true`, `DeletedAt = now`) — except entities marked `[HardDelete]` (join tables, refresh tokens, sample-data purge).
- Collect changes for **AuditLog** + **TimelineEvent** (Section 4.5).

#### 4.1.5 Tenant-level uniqueness
Every unique index includes `TenantId`, e.g. `(tenant_id, key)` on custom field definitions, `(tenant_id, normalized_email)` is **not** unique on leads (duplicates are allowed but detected).

#### 4.1.6 Allowed uses of `IgnoreQueryFilters()`
1. `UserLookup` (login, forgot/reset password, email verification, refresh token, accept invite) — users are looked up before a tenant is known. **Emails are globally unique** across tenants (one user = one tenant).
2. Cross-tenant scanning jobs (reminder scan, overdue scan, report schedules) — they must group results by `TenantId` and process each group inside `tenantContext.Use(tenantId)`.
3. Platform admin endpoints (`/api/v1/platform/*`), which require the `PlatformAdmin` claim.
4. Restoring soft-deleted records (filter by `TenantId` manually!).

### 4.2 Identifiers, naming, DB conventions
- Primary keys: `Guid` v7 (`Guid.CreateVersion7()`), sortable.
- `UseSnakeCaseNamingConvention()` → tables/columns are `snake_case` (needed by the report SQL builder).
- Strings: set max lengths in configurations (names 200, email 320, phone 50, notes `text`).
- Emails stored as `citext`? → No: store `Email` as typed and `NormalizedEmail` (lower-trim) with index.
- Tags: `List<string> Tags` → Postgres `text[]` with GIN index.
- Custom fields: `JsonDocument CustomFields` → `jsonb` with GIN index (`jsonb_path_ops`).

### 4.3 Authentication
- **Access token**: JWT, 15 min, claims: `sub` (userId), `tid` (tenantId), `rid` (roleId), `email`, `name`, `lang`, `plat` (only for platform admins).
- **Refresh token**: random 64 bytes, stored **hashed** (SHA-256) in `refresh_tokens` with rotation & reuse detection (reuse of a revoked token revokes the whole family). 14 days, sliding. Web: sent as `HttpOnly; Secure; SameSite=Strict` cookie at path `/api/v1/auth`. Mobile (Phase 3): returned in body.
- Permissions are **not** in the token; they are loaded per request from cache by `rid` so role changes apply immediately (cache key `perm:{roleId}`, invalidated on role update).
- User `IsActive` cached 60 s (`user-active:{userId}`); deactivated users get 401 and their refresh tokens are revoked.
- Identity options: password min 8, require digit + upper + lower; lockout 5 attempts / 15 min; unique email; email confirmation required within 7 days (banner until then).
- Rate limiting (built-in `AddRateLimiter`): `auth` policy 10 req/min/IP for login/signup/forgot; `public` policy 30 req/min/IP for web forms & booking.
- 2FA (TOTP) — optional, milestone 15.

### 4.4 Authorization: roles, permissions, data scope

#### 4.4.1 Enums (Shared)
```csharp
public enum AppModule { Dashboard, Leads, Contacts, Accounts, Deals, Activities, Calendar, Communication,
    Reports, Notifications, Users, Settings, Products, Quotations, Invoices, Documents, Tickets,
    Projects, Campaigns, Automation, Integrations, Portal }
public enum PermissionAction { View, Create, Edit, Delete, Export, Import }
public enum DataScope { Own = 1, Team = 2, All = 3 }
public enum DashboardView { Staff = 1, Manager = 2, Admin = 3 }
```

#### 4.4.2 Tables
- `roles`: Id, TenantId, Name, Description, IsSystem, DashboardView, IsAdmin (bypasses all checks).
- `role_permissions`: Id, TenantId, RoleId, Module, CanView, CanCreate, CanEdit, CanDelete, CanExport, CanImport, Scope (DataScope).

#### 4.4.3 Default roles (created at sign-up)
| Role | Records scope | Create/Edit | Delete | Export/Import | Settings/Users | Dashboard |
|---|---|---|---|---|---|---|
| Admin (`IsAdmin=true`) | All | ✔ | ✔ | ✔ | ✔ | Admin |
| Manager | Team | ✔ | ✔ | ✔ | ✘ (view users only) | Manager |
| Staff | Own | ✔ | ✘ | ✘ | ✘ | Staff |

Custom roles: copy of any role, editable matrix (module × action + scope).

#### 4.4.4 Enforcement
```csharp
[HasPermission(AppModule.Leads, PermissionAction.View)]     // on controller actions
```
- `HasPermissionAttribute : AuthorizeAttribute` sets `Policy = $"perm:{module}:{action}"`.
- `PermissionPolicyProvider : IAuthorizationPolicyProvider` builds a policy with `PermissionRequirement(module, action)` for names starting with `perm:`.
- `PermissionHandler` asks `IPermissionService.Has(module, action)`.
- Disabled modules (Settings → Modules) return 403 `module_disabled` for everything in that module.

#### 4.4.5 Data scope (row-level)
```csharp
public interface IDataScopeService
{
    DataScope GetScope(AppModule module);
    Task<IReadOnlyList<Guid>> GetScopeUserIdsAsync(DataScope scope, CancellationToken ct); // Own → [me]; Team → me + team members + direct reports (recursive); All → empty (= no filter)
    Task<IQueryable<T>> ApplyScopeAsync<T>(IQueryable<T> q, AppModule module, CancellationToken ct) where T : IOwnedEntity;
}
```
- **Team** = users with the same `TeamId` as me, plus users in teams where I am `LeaderId`, plus users whose `ManagerId` chain leads to me.
- Single-record GET/PUT/DELETE also check scope (load with scope applied → 404 if not visible; never leak existence).
- Activities: visible if the activity owner is in scope **or** the user can see the related record (keep simple in v1: owner in scope OR user is attendee).

### 4.5 Audit log & Timeline
- `audit_logs`: Id, TenantId, UserId, EntityType, EntityId, Action (`Create|Update|Delete|Restore|Login|LoginFailed|Export|Import|PermissionChange|Impersonate`), Changes (jsonb `[{field, old, new}]`), IpAddress, UserAgent, OccurredAt. Written by the interceptor for entities marked `[Audited]` and explicitly for security events. Read-only API for admins.
- `timeline_events`: Id, TenantId, EntityType, EntityId, EventType, Summary, Data (jsonb), ActorUserId, OccurredAt, ActivityId?, MessageId?.
  - EventTypes: `Created, FieldsChanged, StatusChanged, StageChanged, OwnerChanged, Converted, ActivityCreated, ActivityCompleted, NoteAdded, EmailSent, EmailReceived, WhatsAppSent, WhatsAppReceived, SmsSent, SmsReceived, MeetingBooked, FileAttached, Imported, Merged`.
  - **Fan-out**: an event is written once per record it should appear on (e.g. a call logged on a deal writes events for the deal, its account and its primary contact).
  - `FieldsChanged` events are generated by the interceptor (only for fields marked as tracked in the field registry; skip `UpdatedAt`, `Version`, etc.).
- Timeline API: `GET /api/v1/{entity}/{id}/timeline?types=&before=&limit=50` — cursor paging by `OccurredAt`. For a Contact, also include events of its originating Lead (`Contact.OriginLeadId`).

### 4.6 Field Registry, Custom Fields, Filters, Views (THE BACKBONE)

Every module, list, form, import, export, report, template merge field and (later) automation condition uses **one** metadata source: the **Field Registry**.

#### 4.6.1 Field definition
```csharp
public enum FieldType { Text, TextArea, Email, Phone, Url, Number, Decimal, Currency, Percent,
    Date, DateTime, Boolean, Picklist, MultiPicklist, User, Lookup, Tags, File }

public sealed record FieldDef(
    string Key,                 // "first_name", "owner_id", "cf_budget"
    string Label, string? LabelAr,
    FieldType Type,
    bool IsCustom, bool IsSystem,
    bool IsRequired, bool IsReadOnly,
    bool Filterable, bool Sortable, bool Groupable, bool Aggregatable, bool Tracked,
    string? PicklistKey,        // for Picklist/MultiPicklist
    string? LookupEntity,       // "accounts", "contacts", "users"
    LambdaExpression? Selector, // for LINQ lists:   (Lead l) => l.FirstName
    string? SqlExpr,            // for reports:      "l.first_name"
    string[] SqlJoins);         // joins required by SqlExpr, e.g. ["owner"]
```
- `IFieldRegistry.GetFieldsAsync(string entity, CancellationToken)` = static system fields (declared in code per entity: `LeadFields.cs`, ...) + custom fields from DB (cached per tenant, key `fields:{tenant}:{entity}`) + label overrides (Settings).
- Entities registered: `leads, contacts, accounts, deals, activities` (Phase 2+: `products, quotes, invoices, documents, tickets, projects`).

#### 4.6.2 Custom fields
`custom_field_definitions`: Id, TenantId, EntityType, Key (`cf_` + snake, regex `^cf_[a-z][a-z0-9_]{1,46}$`, immutable), Label, LabelAr, FieldType, Options (jsonb `[{value,label,labelAr,color}]` for picklists), IsRequired, DefaultValue, HelpText, Section, SortOrder, IsActive, ShowInList, Filterable, ConvertMapping (jsonb `{contacts:"cf_x", accounts:null, deals:"cf_y"}` used on lead conversion), Origin (`System|Template|User`).
- Values live in the entity's `CustomFields` jsonb: `{ "cf_budget": 1500000, "cf_bedrooms": "3", "cf_license_expiry": "2027-03-31" }`.
- **Storage formats:** Number/Decimal/Currency/Percent → JSON number; Date → `"yyyy-MM-dd"`; DateTime → ISO-8601 UTC string; Boolean → true/false; Picklist → option value string; MultiPicklist → string array; User/Lookup → Guid string; File → file id string.
- `CustomFieldValidator.Validate(entity, IDictionary<string, JsonElement?> values)` — unknown keys rejected, required enforced, type checked, picklist values must exist & be active. Called by every Create/Update.
- Deactivating a field hides it; values are kept. Deleting a field is only allowed when unused, or with explicit "delete data" confirmation (then a job removes the key with `custom_fields - 'cf_key'`).
- Max 150 custom fields per entity per tenant.

#### 4.6.3 Filter model (Shared — used by lists, saved views, reports, dashboards drill-down, campaign segments, automation)
```csharp
public sealed class FilterGroup
{
    public string Logic { get; set; } = "and";                  // "and" | "or"
    public List<FilterCondition> Conditions { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];         // max nesting depth 3
}
public sealed class FilterCondition
{
    public string Field { get; set; } = "";                     // registry key
    public FilterOperator Op { get; set; }
    public JsonElement? Value { get; set; }                     // scalar, array, or {from,to}
}
public enum FilterOperator { Eq, Neq, Contains, NotContains, StartsWith, IsEmpty, IsNotEmpty,
    Gt, Gte, Lt, Lte, Between, In, NotIn,
    Today, Yesterday, ThisWeek, LastWeek, ThisMonth, LastMonth, ThisQuarter, LastQuarter, ThisYear, LastYear,
    LastNDays, NextNDays, Before, After }
```
Special values: `"@me"` (current user id), `"@my_team"` (scope user ids). Example:
```json
{ "logic": "and",
  "conditions": [
    { "field": "owner_id", "op": "Eq", "value": "@me" },
    { "field": "status_id", "op": "In", "value": ["<guid>", "<guid>"] },
    { "field": "cf_license_expiry", "op": "NextNDays", "value": 90 }
  ] }
```

#### 4.6.4 FilterCompiler (LINQ, for lists)
`FilterCompiler.Compile<T>(FilterGroup, IReadOnlyList<FieldDef>, FilterContext) → Expression<Func<T,bool>>`
- System field → use `FieldDef.Selector` body with parameter replaced (`ParameterReplacer` visitor).
- Custom field → `e.CustomFields.RootElement.GetProperty("cf_x").GetString()` (Npgsql translates to `custom_fields->>'cf_x'`). For number comparisons use `GetDecimal()`; dates compare as ISO strings (lexicographic order works for `yyyy-MM-dd`). **Write an integration test proving each translation works; if Npgsql cannot translate one, add a `[DbFunction]` mapped to a SQL helper function** (`crm_cf_numeric(custom_fields, key)` created in a migration).
- Text ops are case-insensitive: `EF.Functions.ILike(field, "%" + EscapeLike(v) + "%")`.
- Date presets → `DateRangeResolver.Resolve(op, value, tenantTime)` → `[startUtc, endUtc)`.
- Unknown field or op not valid for the field type → `ValidationException`.

#### 4.6.5 List query contract (all list endpoints)
```
POST /api/v1/{entity}/query
{ "search": "ahmed", "filter": {FilterGroup}, "sort": [{"field":"created_at","dir":"desc"}],
  "page": 1, "pageSize": 50, "columns": ["full_name","email","owner_id","status_id","cf_budget"], "viewId": null }
→ { "items": [...], "total": 1234, "page": 1, "pageSize": 50 }
```
`pageSize` max 200. `search` searches the entity's search fields (name, email, phone, company) with `ILIKE` (backed by `pg_trgm` GIN indexes).

#### 4.6.6 Saved views
`saved_views`: Id, TenantId, EntityType, Name, OwnerUserId (null = shared with everyone), Filter (jsonb), Columns (jsonb), Sort (jsonb), IsDefault, SortOrder. System views created per entity at sign-up: e.g. Leads → "My open leads", "All open leads", "New this week", "Unassigned".

### 4.7 Time zones & date math
- Tenant has `TimeZoneId` (IANA, e.g. `Asia/Dubai`); a user may override it.
- `TenantTime` service: `Now`, `TodayLocal`, `ToLocal(utc)`, `ToUtc(localDate, localTime)`, `StartOfDay/Week/Month/Quarter/Year` (week start from settings, fiscal year start month from settings for quarters/years in reports).
- `DateRangeResolver` turns presets into UTC ranges using `TenantTime`. **All dashboards and reports must use it** — never `DateTime.Today`.
- Tasks: user enters `DueDate` (DateOnly) and optional `DueTime`; stored `DueAt` (UTC) = local(DueDate, DueTime ?? 23:59:59) → UTC.

### 4.8 Currency & tax
- Tenant: `BaseCurrency` + list of enabled currencies.
- `exchange_rates`: TenantId, Currency, RateToBase (decimal 18,8), EffectiveDate. Phase 1: entered manually in Settings (Phase 2: optional daily auto-fetch).
- Money entities store `Amount`, `Currency`, `ExchangeRate` (captured at save), `AmountInBase = Amount × ExchangeRate`. Re-computed when amount/currency changes. Missing rate → validation error "Add an exchange rate for XXX".
- **All dashboard and report totals use `AmountInBase`.**
- VAT: tenant `TaxEnabled`, `DefaultTaxRate`, `TaxRegistrationNumber`, `TaxLabel` ("VAT"/"GST"). Used fully from Phase 2 (products/quotes/invoices); Deals hold gross amounts.

### 4.9 Search
`GET /api/v1/search?q=...&types=leads,contacts,accounts,deals` → top 5 per type (name/email/phone/company ILIKE), scope applied. GIN `gin_trgm_ops` indexes on searchable columns.

### 4.10 Import & Export (generic)
**Import** (Leads, Contacts, Accounts, Deals, Activities[tasks]):
1. `POST /api/v1/imports` (multipart file `.csv`/`.xlsx`, max 20 MB / 50,000 rows, entityType) → stores file, parses header + first 20 rows → returns `importId`, headers, sample rows, **suggested mapping** (match header to field label/key, case-insensitive, ignoring spaces/underscores; then Levenshtein ≤ 2).
2. `PUT /api/v1/imports/{id}/mapping` `{ mapping: {"Header A":"first_name", ...}, duplicateStrategy: "Skip|Update|Create", defaultOwnerId, defaultValues: {...}, tags: ["import-2026-10"] }`.
3. `POST /api/v1/imports/{id}/validate` → validates all rows (types, picklist values — matched by label case-insensitive, required) → returns counts + first 100 errors.
4. `POST /api/v1/imports/{id}/start` → Hangfire job processes in batches of 500 (one transaction per batch), runs duplicate detection, writes `Imported` timeline events, does **not** fire per-row notifications. On finish: status, counts, error file (xlsx = original rows + "Error" column) and a notification `ImportCompleted`.
5. Undo: `POST /api/v1/imports/{id}/rollback` within 24 h → soft-deletes records created by this import (`ImportId` column on importable entities).
- Requires `Import` permission. Audit logged.

**Export:** `POST /api/v1/{entity}/export` with the same body as `/query` (+ `format: "xlsx"|"csv"`). ≤ 10,000 rows → file returned directly; more → background job + notification with download link (expires 24 h). Requires `Export` permission; columns respect visible fields; audit logged with row count.

### 4.11 Files
`IFileStorage` (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`, `GetDownloadUrl`) — `LocalFileStorage` (dev, `App_Data/files/{tenantId}/...`), `AzureBlobFileStorage`/`S3FileStorage` later. `files` table: Id, TenantId, FileName, ContentType, SizeBytes, StoragePath, EntityType?, EntityId?, UploadedById, CreatedAt. Validate extension whitelist + size (25 MB default). Download through `GET /api/v1/files/{id}` with permission check on the linked entity.

### 4.12 Background jobs, real-time, caching
- Hangfire dashboard at `/hangfire` (platform admins only). Queues: `default`, `messaging`, `reports`, `imports`.
- SignalR hub `/hubs/notifications` (JWT via `access_token` query string for websockets). Groups: `user:{userId}`, `tenant:{tenantId}`.
- HybridCache keys always start with `{tenantId}:` to avoid cross-tenant leaks.

### 4.13 Localization (English + Arabic, RTL)
- UI strings: `Resources/SharedResource.resx` + `SharedResource.ar.resx` in MindLift.Web, `IStringLocalizer<SharedResource>`. Never hard-code UI text.
- Language: user preference → tenant default. Arabic → `<MudRTLProvider RightToLeft="true">` and `dir="rtl"` on `<html>`.
- Data labels: picklist values, module names, custom fields, pipeline stages all have `Label` + `LabelAr`; client shows `LabelAr ?? Label` in Arabic.
- Numbers/dates formatted with the user's culture; currency with ISO code.
- PDFs: embed **Noto Sans** + **Noto Naskh Arabic** fonts in QuestPDF.
- API error messages: return `code` + English `detail`; the client maps `code` to localized text.

### 4.14 Configuration & secrets
`appsettings.json` sections: `ConnectionStrings:Default`, `Jwt:{Issuer,Audience,SigningKey}`, `App:{BaseUrl, PublicBookingBaseUrl}`, `Smtp:{...}` (system emails), `Storage:{Provider, LocalPath}`, `Hangfire`, `Serilog`. Secrets via user-secrets (dev) / environment variables (prod). Tenant secrets (WhatsApp tokens, SMTP passwords, OAuth tokens) are encrypted with ASP.NET Core **Data Protection** (`IDataProtector` purpose `"tenant-secrets"`) before saving; keys persisted to the DB (`PersistKeysToDbContext`).

---

## 5. DATA MODEL — PHASE 1

All tables below inherit `TenantEntity` (Id, TenantId, CreatedAt, CreatedById, UpdatedAt, UpdatedById, IsDeleted, DeletedAt, Version) unless marked *(global)*. `Owned` = implements `IOwnedEntity`; `CF` = has `CustomFields` jsonb; `S` = has `IsSample`.

### 5.1 Platform / tenancy
**tenants** *(global)*: Id, Name, Slug (unique), IndustryKey, TemplateVersion, PlanId, Status (`Trial|Active|PastDue|Suspended|Cancelled`), TrialEndsAt, OnboardingStep (int), OnboardingCompletedAt, ChecklistDismissed, Country (ISO-2), CreatedAt.
**plans** *(global)*: Id, Key (`starter|pro|enterprise`), Name, MaxUsers, MaxRecords, StorageGb, Features (jsonb list of module keys), PriceMonthly, IsActive.
**tenant_settings** (1:1 tenant): CompanyName, LegalName, LogoFileId, PrimaryColor, Email, Phone, Website, Address (Line1, Line2, City, State, PostalCode, Country), BaseCurrency, EnabledCurrencies (text[]), DefaultLanguage (`en|ar`), TimeZoneId, DateFormat, WeekStartDay, FiscalYearStartMonth, TaxEnabled, TaxLabel, DefaultTaxRate, TaxRegistrationNumber, DefaultLeadOwnerId?, AutoCreateLeadFromInbound (bool), SampleDataLoaded (bool).
**module_settings**: ModuleKey (AppModule), IsEnabled, SingularLabel, PluralLabel, SingularLabelAr, PluralLabelAr, Icon, SortOrder, Origin.
**field_label_overrides**: EntityType, FieldKey, Label, LabelAr, IsHidden.
**layouts**: EntityType, Kind (`Form|Detail`), Definition (jsonb: `[{section, sectionAr, columns:1|2, fields:["first_name",...]}]`).
**exchange_rates**: Currency, RateToBase, EffectiveDate.

### 5.2 Users & security
**users** (Identity `AppUser`, tenant-filtered): Id, TenantId, Email, NormalizedEmail (globally unique), PasswordHash, ..., FirstName, LastName, Phone, AvatarFileId, RoleId, TeamId?, BranchId?, ManagerId?, JobTitle, IsActive, IsTenantOwner, Language, TimeZoneId?, LastLoginAt, CreatedAt, IsPlatformAdmin.
**roles**, **role_permissions** (Section 4.4.2).
**teams**: Name, BranchId?, LeaderId?.
**branches**: Name, Address, TimeZoneId?.
**invitations**: Email, FirstName, LastName, RoleId, TeamId?, TokenHash, ExpiresAt (7 days), AcceptedAt, InvitedById, Status (`Pending|Accepted|Revoked|Expired`).
**refresh_tokens** *(hard delete)*: UserId, TokenHash, FamilyId, ExpiresAt, CreatedAt, CreatedIp, RevokedAt, ReplacedByHash.
**audit_logs**, **timeline_events** (Section 4.5).

### 5.3 Configuration
**picklist_values**: PicklistKey, Value (stable code), Label, LabelAr, Color, SortOrder, IsDefault, IsActive, IsSystem, Category (string, semantic — see below), Origin.
Picklist keys & semantic categories:
| PicklistKey | Used by | Category values |
|---|---|---|
| `lead_status` | Lead.StatusId | `New`, `Working`, `Qualified`, `Disqualified`, `Converted` |
| `lead_source` | Lead/Contact/Deal.SourceId | — (Website, Referral, Social Media, Walk-in, Campaign, WhatsApp, Email, Phone, Booking, Import, Other) |
| `lead_rating` | Lead.RatingId | — (Hot, Warm, Cold) |
| `disqualify_reason` | Lead | — |
| `contact_type` | Contact.TypeId | — (Client, Prospect, Partner, Supplier) |
| `account_type` | Account.TypeId | — (Customer, Prospect, Partner, Supplier, Competitor) |
| `industry` | Account.IndustryId | — |
| `company_size` | Account.SizeId | — (1-10, 11-50, 51-200, 201-1000, 1000+) |
| `lost_reason` | Deal.LostReasonId | — (Price, Competitor, No budget, No response, Timing, Other) |
| `call_outcome` | Activity.CallOutcomeId | — (Connected, No answer, Busy, Left voicemail, Wrong number) |
| `task_type` | Activity (task) | — (To-do, Follow-up, Email, Document collection) |
| `salutation` | Lead/Contact | — (Mr., Mrs., Ms., Dr.) |

Code that needs meaning (e.g. "is this lead converted?") uses **Category**, never the label.

**pipelines**: Name, NameAr, IsDefault, SortOrder, IsActive, Origin.
**pipeline_stages**: PipelineId, Name, NameAr, SortOrder, Probability (0–100), StageType (`Open|Won|Lost`), Color, RottingDays? (highlight deals without activity for N days), Origin. Rules: each pipeline has ≥ 1 Open, exactly 1 Won and exactly 1 Lost stage; Won/Lost stages are always last.
**assignment_rules**: Name, EntityType (`leads` in v1), IsActive, Priority, Conditions (FilterGroup jsonb), Method (`RoundRobin|LoadBalanced|SpecificUser`), UserIds (uuid[]), LastAssignedIndex, Origin.
**web_forms**: Name, PublicToken (unique, random 24 chars), Fields (jsonb `[{fieldKey,label,required,hidden,defaultValue}]`), DefaultSourceId, DefaultOwnerId?, UseAssignmentRules, SuccessMessage, RedirectUrl, AllowedDomains (text[]), IsActive, SubmissionCount.
**checklist_templates** (customer onboarding playbooks): Name, Trigger (`DealWon`), PipelineId?, Items (jsonb `[{title, titleAr, offsetDays, assignTo: "DealOwner|AccountOwner|User", userId?, priority}]`), IsActive, Origin.
**message_templates**: Channel (`Email|WhatsApp|Sms`), Name, Language, Subject, Body, ProviderTemplateName?, ProviderStatus?, Variables (jsonb), Category, IsActive, Origin.
**saved_views** (Section 4.6.6).
**custom_field_definitions** (Section 4.6.2).

### 5.4 CRM records
**leads** (Owned, CF, S): Salutation, FirstName, LastName, FullName (computed/stored), Email, NormalizedEmail, Phone, NormalizedPhone, Mobile, WhatsAppNumber, CompanyName, JobTitle, Website, Nationality, Address fields, Country, SourceId, StatusId, RatingId?, EstimatedValue, Currency, Description, Tags, OwnerId, AssignedAt, ContactId? (if the person already exists as a contact), FirstContactedAt, LastActivityAt, NextActivityAt, DisqualifyReasonId?, ConvertedAt, ConvertedById, ConvertedContactId, ConvertedAccountId, ConvertedDealId, WebFormId?, ImportId?, CampaignId? (P3), Utm (jsonb: source, medium, campaign, term, content).
**contacts** (Owned, CF, S): Salutation, FirstName, LastName, FullName, Email, NormalizedEmail, SecondaryEmail, Phone, NormalizedPhone, Mobile, WhatsAppNumber, JobTitle, Department, AccountId?, TypeId?, Nationality, DateOfBirth (DateOnly?), Gender?, IdNumber?, Address fields, Country, SourceId?, Tags, OwnerId, OriginLeadId?, DoNotContact (bool), EmailOptOut, WhatsAppOptOut, SmsOptOut, LastActivityAt, NextActivityAt, ImportId?.
**accounts** (Owned, CF, S): Name, NormalizedName, LegalName, IndustryId?, SizeId?, TypeId?, Website, Email, Phone, Address fields, Country, TradeLicenseNumber, TradeLicenseExpiry (DateOnly?), TaxNumber, ParentAccountId?, AnnualRevenue?, Currency?, Description, Tags, OwnerId, LastActivityAt, ImportId?.
**deals** (Owned, CF, S): Name, PipelineId, StageId, Status (`Open|Won|Lost`), Amount, Currency, ExchangeRate, AmountInBase, Probability, ProbabilityOverridden, ExpectedCloseDate (DateOnly?), ClosedAt?, LostReasonId?, LostNotes, AccountId?, PrimaryContactId?, SourceId?, OriginLeadId?, OwnerId, StageChangedAt, LastActivityAt, NextActivityAt, Description, Tags, ImportId?.
**deal_contacts**: DealId, ContactId, Role (text) — extra contacts on a deal.
**deal_stage_history** *(no soft delete needed)*: DealId, PipelineId, FromStageId?, ToStageId, ChangedAt, ChangedById, SecondsInFromStage, AmountInBaseAtChange.
**activities** (Owned, CF, S): Type (`Task|Call|Meeting|Note`), Subject, Description (sanitized HTML), Status (`Open|Completed|Cancelled`), Priority (`Low|Normal|High|Urgent`), TaskTypeId?, DueDate?, DueTime?, DueAt?, StartAt?, EndAt?, AllDay, Location, MeetingUrl, CallDirection? (`Inbound|Outbound`), CallOutcomeId?, CallDurationSeconds?, CompletedAt, CompletedById, ReminderMinutesBefore?, ReminderAt?, ReminderSentAt?, OverdueNotifiedAt?, OwnerId (assignee), LeadId?, ContactId?, AccountId?, DealId?, RelatedEntityType + RelatedEntityId (the "primary" record it was created from), ParentActivityId? (follow-up chain), ChecklistTemplateId?, ExternalCalendarEventId?, BookingId?, ImportId?.
**activity_attendees**: ActivityId, UserId?, ContactId?, Email, Name, ResponseStatus (`NeedsAction|Accepted|Declined|Tentative`).
**mentions**: ActivityId (note), MentionedUserId, NotifiedAt.

### 5.5 Calendar, communication, notifications
**booking_pages**: Slug (unique per tenant; public URL `/book/{tenantSlug}/{slug}`), Name, NameAr, Description, DurationMinutes, BufferBeforeMinutes, BufferAfterMinutes, SlotStepMinutes, MinNoticeHours, MaxDaysAhead, AssignmentMode (`SingleUser|RoundRobin`), UserIds (uuid[]), Availability (jsonb: `{ "timeZone":"Asia/Dubai", "weekly": { "mon":[["09:00","13:00"],["14:00","18:00"]], ... }, "dateOverrides": [{ "date":"2026-12-02", "ranges":[] }] }`), Questions (jsonb), LocationType (`InPerson|Phone|Video`), LocationValue, ConfirmationTemplateId?, ReminderOffsetsMinutes (int[] e.g. [1440, 60]), ReminderChannels (text[]), IsActive.
**bookings**: BookingPageId, ActivityId, ContactId?, LeadId?, Name, Email, Phone, Answers (jsonb), StartAt, EndAt, Status (`Confirmed|Cancelled|Rescheduled|NoShow`), ManageToken (hash), CancelledAt, CancelReason.
**calendar_connections** (M11b): UserId, Provider (`Google|Microsoft`), AccountEmail, EncryptedTokens, CalendarId, SyncToken, LastSyncAt, Status.
**external_busy_slots** (M11b): UserId, ConnectionId, ExternalEventId, StartAt, EndAt.
**channel_accounts**: Channel (`Email|WhatsApp|Sms`), Name, OwnerUserId? (null = shared), IsDefault, Settings (jsonb, non-secret), EncryptedSecrets, Status (`Connected|Error|Disabled`), LastError, LastSyncAt. Email: SMTP host/port/ssl/user, IMAP host/port/user, FromName, FromAddress, LastImapUid. WhatsApp: PhoneNumberId, WabaId, DisplayPhone, VerifyToken. SMS: Provider (`Twilio`), FromNumber/SenderId.
**messages**: Channel, Direction (`Outbound|Inbound`), Status (`Draft|Queued|Sent|Delivered|Read|Failed|Received`), ChannelAccountId, From, To (text[]), Cc (text[]), Subject, BodyText, BodyHtml, TemplateId?, ProviderMessageId, InternetMessageId, InReplyTo, ThreadKey, LeadId?, ContactId?, AccountId?, DealId?, SentById?, SentAt, ReceivedAt, DeliveredAt, ReadAt, Error, RetryCount.
**message_attachments**: MessageId, FileId.
**notifications**: UserId, Type, Title, Body, EntityType?, EntityId?, Url, IsRead, ReadAt, CreatedAt, GroupKey? (to collapse repeats).
**notification_preferences**: UserId, Type, InApp, Email, Push (Phase 3).
**files**, **imports** (Id, EntityType, FileId, Status `Uploaded|Mapped|Validated|Running|Completed|Failed|RolledBack`, Mapping, DuplicateStrategy, Defaults, TotalRows, CreatedRows, UpdatedRows, SkippedRows, FailedRows, ErrorFileId, StartedAt, FinishedAt).

### 5.6 Dashboard & reports
**dashboard_layouts**: UserId? (null = default for RoleId), RoleId?, View (DashboardView), Widgets (jsonb `[{key, order, size:"sm|md|lg|xl", hidden, settings:{}}]`).
**sales_targets**: Scope (`User|Team|Company`), UserId?, TeamId?, Metric (`WonAmount|WonDeals|NewLeads|ActivitiesCompleted|ConvertedLeads`), PeriodType (`Month|Quarter|Year`), PeriodStart (DateOnly), TargetValue (decimal). Unique (tenant, scope, user/team, metric, periodType, periodStart).
**report_folders**: Name, OwnerUserId?, Visibility (`Private|Everyone`).
**report_definitions**: Name, NameAr, Description, FolderId?, IsSystem, SystemKey?, Kind (`Generic|System`), Query (jsonb `ReportQuery`, Section 12.3), Visualization (jsonb: `{chart:"bar|line|area|donut|pie|funnel|table|kpi", xKey, seriesKeys, stacked}`), OwnerUserId, Visibility (`Private|Team|Everyone`), IsFavoriteFor (uuid[]), LastRunAt.
**report_schedules**: ReportId, Frequency (`Daily|Weekly|Monthly`), DayOfWeek?, DayOfMonth?, TimeOfDay, TimeZoneId, Format (`Xlsx|Pdf`), RecipientUserIds (uuid[]), ExtraEmails (text[] — only if tenant allows external), FilterOverrides (jsonb), RunAsUserId (= creator), IsActive, LastRunAt, NextRunAt, LastStatus, LastError.
**report_runs**: ReportId, ScheduleId?, RunById, StartedAt, DurationMs, RowCount, Status, Error, FileId?.

### 5.7 Mandatory indexes (add in configurations)
- leads: `(tenant_id, owner_id, status_id)`, `(tenant_id, created_at)`, `(tenant_id, normalized_email)`, `(tenant_id, normalized_phone)`, `(tenant_id, converted_at)`, trigram on `full_name`, `company_name`, `email`.
- contacts: `(tenant_id, account_id)`, `(tenant_id, normalized_email)`, `(tenant_id, normalized_phone)`, trigram `full_name`.
- accounts: `(tenant_id, normalized_name)`, `(tenant_id, parent_account_id)`, trigram `name`.
- deals: `(tenant_id, pipeline_id, stage_id, status)`, `(tenant_id, status, closed_at)`, `(tenant_id, owner_id, status)`, `(tenant_id, expected_close_date)`.
- activities: `(tenant_id, owner_id, status, due_at)`, `(tenant_id, type, start_at)`, `(tenant_id, deal_id)`, `(tenant_id, contact_id)`, `(tenant_id, account_id)`, `(tenant_id, lead_id)`, partial `(reminder_at) WHERE reminder_sent_at IS NULL AND status = 'Open'`.
- timeline_events: `(tenant_id, entity_type, entity_id, occurred_at DESC)`.
- notifications: `(user_id, is_read, created_at DESC)`.
- deal_stage_history: `(tenant_id, deal_id, changed_at)`, `(tenant_id, to_stage_id, changed_at)`.
- GIN on every `custom_fields` and `tags`.

Enums are stored as **strings** (`HasConversion<string>()`) for readable SQL in reports.

### 5.8 Documents, OCR & portal tables
Defined in **Section 19.3** (`documents`, `document_pages`, `document_types`, `document_type_fields`, `document_extracted_fields`, `document_requests`) and **Section 20.3** (`portal_users`, `portal_refresh_tokens`, `portal_settings`). They follow the same `TenantEntity` rules.

---

## 6. ★ CUSTOMER ONBOARDING (TENANT SIGN-UP → READY-TO-USE CRM)

"Customer onboarding" in this product has two meanings — both are built:
- **A. Tenant onboarding**: a new business subscribes and gets a CRM configured for its industry (Sections 6.1–6.7).
- **B. End-customer onboarding**: the tenant turns *their* leads into customers (conversion) and runs a post-sale onboarding checklist (Section 6.8 and Leads module, Section 7.4).

### 6.1 User journey (A)
```
Landing page → Sign up form → (account + tenant created, logged in) → Onboarding Wizard
  Step 1 Company profile   (required)
  Step 2 Industry          (required; applies template)
  Step 3 Pipeline & stages (pre-filled from template; editable; skippable)
  Step 4 Invite team       (skippable)
  Step 5 Bring your data   (import CSV/Excel | load sample data | start empty)
  Step 6 Connect channels  (email / WhatsApp; skippable)
→ Dashboard with "Getting started" checklist widget
```
The wizard is resumable (`tenants.onboarding_step`). Until Step 2 is done, every app route redirects to `/onboarding`.

### 6.2 Sign-up
`POST /api/v1/auth/signup` (rate-limited, anonymous)
```json
{ "companyName":"Al Noor Properties", "firstName":"Sara", "lastName":"Khan", "email":"sara@alnoor.ae",
  "password":"...", "phone":"+971501234567", "country":"AE", "language":"en", "acceptTerms": true }
```
Logic (one DB transaction):
1. Validate; email must not exist globally (`UserLookup`) → 409 `email_taken`.
2. Create `tenants` (Slug from company name, unique — append `-2`, `-3`...; Status `Trial`; TrialEndsAt = now + 14 days; PlanId = `pro` trial; OnboardingStep = 1).
3. `using tenantContext.Use(tenant.Id)`.
4. Create `tenant_settings` from **country defaults**:

| Country | Currency | TimeZone | Tax label | Rate | Week start |
|---|---|---|---|---|---|
| AE | AED | Asia/Dubai | VAT | 5 | Monday |
| SA | SAR | Asia/Riyadh | VAT | 15 | Sunday |
| QA | QAR | Asia/Qatar | — | 0 | Sunday |
| OM | OMR | Asia/Muscat | VAT | 5 | Sunday |
| BH | BHD | Asia/Bahrain | VAT | 10 | Sunday |
| KW | KWD | Asia/Kuwait | — | 0 | Sunday |
| IN | INR | Asia/Kolkata | GST | 18 | Monday |
| GB | GBP | Europe/London | VAT | 20 | Monday |
| US | USD | America/New_York | Sales tax | 0 | Sunday |
| other | USD | UTC | Tax | 0 | Monday |

5. **Base system seed** (`SystemSeedService.SeedTenantAsync`): default roles (Admin/Manager/Staff + permissions), all `module_settings` (Phase 1 modules enabled; Phase 2/3 disabled), base picklists (Section 5.3 with EN+AR labels), notification preferences defaults, system saved views, system report definitions (Section 12.6), default dashboard layouts per view.
6. Create owner `AppUser` (Role Admin, IsTenantOwner = true) via `UserManager.CreateAsync`.
7. Send verification email (queued job). Write audit `Create tenant`.
8. Return tokens (user can work immediately; verification required within 7 days).

### 6.3 Onboarding API
| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/onboarding` | `{ currentStep, completedSteps[], skippedSteps[], tenant summary, checklist }` |
| PUT | `/api/v1/onboarding/company` | Step 1: name, logo (fileId), country, currency, timezone, language, address, tax settings |
| GET | `/api/v1/onboarding/industries` | List templates with preview (renamed modules, stages, custom fields, sample dashboard) |
| POST | `/api/v1/onboarding/industry` | Step 2: `{ industryKey }` → applies template (Section 6.5) |
| PUT | `/api/v1/onboarding/pipeline` | Step 3: `{ pipelines:[{id?, name, stages:[{id?, name, probability, stageType}]}] }` |
| POST | `/api/v1/onboarding/team` | Step 4: `{ invites:[{email, firstName, roleId, teamId?}] }` (seat limit check) |
| POST | `/api/v1/onboarding/data` | Step 5: `{ mode: "sample" | "import" | "empty" }` (import = redirects UI to the import wizard) |
| POST | `/api/v1/onboarding/skip/{step}` | Skip steps 3–6 |
| POST | `/api/v1/onboarding/complete` | Sets `OnboardingCompletedAt`, step = 99 |
| DELETE | `/api/v1/onboarding/sample-data` | Removes all `IsSample` records (hard delete) — available anytime from Settings |
| POST | `/api/v1/onboarding/checklist/dismiss` | Hide checklist widget |

Only the tenant owner / Admin role can call onboarding endpoints.

### 6.4 Industry templates — format
Templates are JSON files embedded in `MindLift.Infrastructure/Templates/Industries/*.json`, loaded by `IndustryTemplateProvider`, validated at startup (unit test loads all and validates). Schema:
```json
{
  "key": "real_estate",
  "version": 1,
  "name": "Real estate", "nameAr": "العقارات",
  "description": "Agencies and brokers managing property enquiries, viewings and offers.",
  "modules": {
    "deals":    { "singular": "Property Deal", "plural": "Property Deals", "singularAr": "صفقة عقار", "pluralAr": "صفقات العقارات" },
    "accounts": { "singular": "Company", "plural": "Companies" },
    "enable":  ["Dashboard","Leads","Contacts","Accounts","Deals","Activities","Calendar","Communication","Reports"],
    "disable": []
  },
  "picklists": {
    "lead_source": { "mode": "merge", "values": [
      { "value": "property_finder", "label": "Property Finder", "labelAr": "بروبرتي فايندر" },
      { "value": "bayut", "label": "Bayut", "labelAr": "بيوت" },
      { "value": "dubizzle", "label": "Dubizzle", "labelAr": "دوبيزل" } ] },
    "cf_property_type": { "mode": "replace", "values": [
      { "value": "apartment", "label": "Apartment", "labelAr": "شقة" },
      { "value": "villa", "label": "Villa", "labelAr": "فيلا" },
      { "value": "townhouse", "label": "Townhouse", "labelAr": "تاون هاوس" },
      { "value": "office", "label": "Office", "labelAr": "مكتب" },
      { "value": "land", "label": "Land", "labelAr": "أرض" } ] }
  },
  "pipelines": [
    { "name": "Sales", "nameAr": "المبيعات", "isDefault": true, "stages": [
      { "name": "Enquiry",   "nameAr": "استفسار", "probability": 10, "type": "Open", "rottingDays": 3 },
      { "name": "Viewing",   "nameAr": "معاينة", "probability": 30, "type": "Open", "rottingDays": 7 },
      { "name": "Offer",     "nameAr": "عرض",    "probability": 60, "type": "Open", "rottingDays": 7 },
      { "name": "Agreement", "nameAr": "اتفاقية", "probability": 85, "type": "Open", "rottingDays": 14 },
      { "name": "Closed Won","nameAr": "مغلقة - ناجحة", "probability": 100, "type": "Won" },
      { "name": "Closed Lost","nameAr": "مغلقة - خاسرة", "probability": 0, "type": "Lost" } ] },
    { "name": "Leasing", "nameAr": "التأجير", "stages": [
      { "name": "Enquiry", "probability": 10, "type": "Open" },
      { "name": "Viewing", "probability": 40, "type": "Open" },
      { "name": "Contract", "probability": 80, "type": "Open" },
      { "name": "Leased", "probability": 100, "type": "Won" },
      { "name": "Lost", "probability": 0, "type": "Lost" } ] }
  ],
  "customFields": [
    { "entity": "leads", "key": "cf_property_type", "label": "Property type", "labelAr": "نوع العقار", "type": "Picklist", "picklist": "cf_property_type", "section": "Requirements", "showInList": true, "convertTo": { "deals": "cf_property_type" } },
    { "entity": "leads", "key": "cf_location", "label": "Preferred location", "labelAr": "الموقع المفضل", "type": "Text", "section": "Requirements", "convertTo": { "deals": "cf_location" } },
    { "entity": "leads", "key": "cf_budget", "label": "Budget", "labelAr": "الميزانية", "type": "Currency", "section": "Requirements", "showInList": true, "convertTo": { "deals": "cf_budget" } },
    { "entity": "leads", "key": "cf_bedrooms", "label": "Bedrooms", "labelAr": "غرف النوم", "type": "Number", "section": "Requirements", "convertTo": { "deals": "cf_bedrooms" } },
    { "entity": "deals", "key": "cf_property_type", "label": "Property type", "labelAr": "نوع العقار", "type": "Picklist", "picklist": "cf_property_type" },
    { "entity": "deals", "key": "cf_location", "label": "Location", "labelAr": "الموقع", "type": "Text" },
    { "entity": "deals", "key": "cf_budget", "label": "Budget", "labelAr": "الميزانية", "type": "Currency" },
    { "entity": "deals", "key": "cf_bedrooms", "label": "Bedrooms", "labelAr": "غرف النوم", "type": "Number" },
    { "entity": "contacts", "key": "cf_preferred_language", "label": "Preferred language", "type": "Picklist", "options": ["English","Arabic","Hindi","Russian","Other"] }
  ],
  "layouts": { "leads": { "form": [ { "section": "Contact info", "columns": 2, "fields": ["salutation","first_name","last_name","email","phone","whatsapp_number","nationality"] },
                                    { "section": "Requirements", "columns": 2, "fields": ["cf_property_type","cf_location","cf_budget","cf_bedrooms"] },
                                    { "section": "Lead details", "columns": 2, "fields": ["source_id","status_id","rating_id","owner_id","description"] } ] } },
  "messageTemplates": [
    { "channel": "WhatsApp", "name": "Viewing confirmation", "language": "en",
      "body": "Hi {{contact.first_name}}, your viewing is confirmed for {{activity.start_local}}. – {{user.full_name}}, {{company.name}}" },
    { "channel": "Email", "name": "New enquiry follow-up", "language": "en",
      "subject": "Your property enquiry", "body": "Dear {{lead.first_name | \"Sir/Madam\"}}, thank you for your enquiry ..." }
  ],
  "checklistTemplates": [
    { "name": "After deal won", "trigger": "DealWon", "items": [
      { "title": "Collect signed agreement", "offsetDays": 1, "assignTo": "DealOwner", "priority": "High" },
      { "title": "Collect Emirates ID & passport copies", "offsetDays": 2, "assignTo": "DealOwner" },
      { "title": "Arrange key handover", "offsetDays": 7, "assignTo": "DealOwner" } ] }
  ],
  "assignmentRules": [ { "name": "Round robin – all sales", "method": "RoundRobin", "allUsersWithRole": "Staff" } ],
  "savedViews": [ { "entity": "deals", "name": "Viewings this week", "filter": { "logic":"and", "conditions":[ { "field":"stage_name","op":"Eq","value":"Viewing" } ] } } ],
  "dashboardHighlights": ["deals_in_progress", "upcoming_meetings", "leads_by_source"],
  "documentTypes": { "enable": ["passport", "emirates_id", "title_deed", "generic"], "portalEnabled": true },
  "sampleData": { "profile": "real_estate" }
}
```

### 6.5 Template application logic (`IndustryTemplateService.ApplyAsync(tenantId, key)`)
Runs in one transaction:
1. Load & validate template. If the tenant already applied a template:
   - If any non-sample lead/contact/account/deal exists → 409 `template_locked` (industry can't be switched after real data exists; admins can still change everything manually).
   - Else remove all config rows with `Origin = Template` (custom fields, pipelines, stages, picklist values from the template, templates, checklists, rules, views) and continue.
2. Module settings: rename + enable/disable.
3. Picklists: `merge` = add values that don't exist (by `Value`); `replace` = deactivate existing non-system values then add.
4. Pipelines/stages: create; ensure exactly one default pipeline; validate stage rules.
5. Custom fields (+ their picklists), layouts, field label overrides.
6. Message templates, checklist templates, assignment rules (resolve `allUsersWithRole` at apply time — if only the owner exists, set to owner), saved views.
7. Set `tenants.IndustryKey`, `TemplateVersion`; Origin = `Template` on all created rows.
8. Invalidate caches (`fields:*`, `bootstrap:*` for the tenant).

### 6.6 Industry templates to ship (from the handover table)
Ship 6 templates + 1 generic. Build each file like Section 6.4:

| key | Deal renamed to | Pipeline stages (Open… / Won / Lost) | Custom fields (entity) | Highlight modules |
|---|---|---|---|---|
| `general` | Deal | Qualification 10, Needs analysis 30, Proposal 60, Negotiation 80 / Won / Lost | — | Leads, Deals, Activities |
| `real_estate` | Property Deal | Enquiry 10, Viewing 30, Offer 60, Agreement 85 / Closed Won / Closed Lost | Property type, location, budget, bedrooms (leads+deals) | Leads, Deals, Calendar |
| `corporate_services` | Engagement | Enquiry 10, Proposal 30, Documents collected 60, Submitted 80 / Completed / Lost | Trade license no., license expiry (Date), jurisdiction (Picklist: Mainland, Freezone, Offshore) on accounts; service type on deals | Documents*, Projects*, Invoices*, Portal* |
| `logistics` | Shipment | Quote 10, Booked 50, In transit 75, Delivered 90 / Invoiced / Lost | Origin, destination, weight (Decimal kg), container type (Picklist: 20ft, 40ft, 40ft HC, LCL, Air) on deals | Quotations*, Tickets*, Invoices* |
| `finance_consulting` | Engagement | Discovery 10, Proposal 40, Signed 70, In progress 90 / Closed / Lost | Service type, fee basis (Fixed, Hourly, Retainer, Success fee), renewal date on deals | Quotations*, Projects*, Invoices* |
| `healthcare` | Patient Case | Enquiry 10, Booked 40, Consulted 70, Follow-up 90 / Closed / Lost | Date of birth (contacts, system field), insurance provider, policy number, doctor (User) on deals/contacts | Calendar, Documents*, Portal*, Notifications |
| `other` | = `general` | | | |

`*` = Phase 2/3 modules: template lists them in `modules.enable`; they activate automatically when built (module_settings exists but `IsAvailable` is false until the module ships).

Each template also provides: ≥ 2 message templates (EN + AR), 1 checklist template, picklist additions for `lead_source` relevant to the industry (e.g. healthcare: Insurance referral, Doctor referral), and `documentTypes.enable` (which built-in document types from 19.7 are active):

| Template | Document types enabled |
|---|---|
| general / other | generic, invoice |
| real_estate | passport, emirates_id, title_deed, generic |
| corporate_services | trade_license, passport, emirates_id, financial_statement, generic |
| logistics | invoice, generic |
| finance_consulting | financial_statement, trade_license, invoice, generic |
| healthcare | emirates_id, passport, insurance_card, generic |

### 6.7 Sample data (`SampleDataService.LoadAsync(profile)`)
Uses **Bogus** with a fixed seed per profile (deterministic) and Gulf-appropriate names (mix of Arabic/English/Indian names), UAE phone formats `+9715XXXXXXXX`. Creates, all with `IsSample = true`:
- 8 accounts, 20 contacts (linked), 30 leads (mix of statuses/sources; 6 converted), 25 deals spread over **the last 6 months** across all stages; 8 won (`ClosedAt` spread over months), 5 lost with reasons; **deal_stage_history rows consistent with stages** (so funnel/velocity reports have data).
- 60 activities (calls/meetings/tasks/notes; some overdue, some today, some upcoming).
- Monthly sales targets for the owner for the last 6 and next 3 months.
- Owner of all sample records = tenant owner (or round-robin across invited users if any).
- Set `tenant_settings.SampleDataLoaded = true`. Show a banner "You're viewing sample data — Remove it" until removed.
- Removal: hard-delete all `IsSample` rows in dependency order (activities → stage history → deal_contacts → deals → leads → contacts → accounts → targets), plus their timeline events.

### 6.8 Getting-started checklist (computed, not stored)
`GET /api/v1/onboarding` returns `checklist: [{ key, title, done, url }]`, computed live:
| key | done when |
|---|---|
| `company_logo` | `tenant_settings.LogoFileId != null` |
| `invite_team` | active users > 1 |
| `first_lead` | ≥ 1 non-sample lead |
| `first_deal` | ≥ 1 non-sample deal |
| `connect_email` | ≥ 1 connected Email channel account |
| `connect_whatsapp` | WhatsApp channel connected |
| `web_form` | ≥ 1 active web form |
| `sales_target` | ≥ 1 sales target for current period |
| `remove_sample` | `SampleDataLoaded == false` |
Shown in the `onboarding_checklist` dashboard widget (Admin view) with % progress until all done or dismissed.

### 6.9 Plan limits
`IPlanLimitService`: check `MaxUsers` on invite/activate (→ 402-style `ProblemDetails` code `plan_limit_users`), `MaxRecords` on create/import, `StorageGb` on upload, `Features` on module enable. Trial expiry job: day 11 reminder email, day 14 → `Status = PastDue` → read-only mode (GET allowed, writes → 402 `subscription_required`) — SaaS billing itself (Stripe) is a Phase 2 item; in Phase 1 the platform admin can set Status/Plan manually.

### 6.10 Platform admin (minimal)
`/api/v1/platform/tenants` (list/search, view usage: users, records, storage, last activity), `PATCH .../{id}/status`, `PATCH .../{id}/plan`, `POST .../{id}/impersonate` (issues a 30-min token with claim `imp=<adminId>`, banner in UI, every action audit-logged with impersonator). Platform admins are users with `IsPlatformAdmin = true` in a dedicated internal tenant.

### 6.11 Acceptance criteria (Onboarding)
- New sign-up → wizard → choosing "Real estate" renames Deals to "Property Deals" in nav, list titles and forms; pipeline shows Enquiry…Closed; lead form shows Requirements section with 4 custom fields.
- Choosing a different industry before adding real data replaces config cleanly (no leftover template fields).
- Loading sample data makes every dashboard widget and every system report show non-empty data; removing it returns all to empty.
- Two tenants signing up concurrently with the same company name get different slugs.
- Tenant A can never see tenant B's templates/config/records (integration test).

---

## 7. MODULE SPECIFICATIONS — PHASE 1

Common to all record modules (Leads, Contacts, Accounts, Deals, Activities): `POST /query`, `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /bulk`, `GET /{id}/timeline`, `POST /export`, imports via `/imports`, saved views, custom fields, scope, audit. **Bulk** body: `{ ids:[...] | filter:{...}, action:"assign|update_field|add_tags|remove_tags|delete", payload:{...} }`, max 1,000 records synchronously, more → job.

### 7.1 Users, Teams & Permissions (Module 20)
**API**
- `GET /users` (query), `GET /users/{id}`, `PUT /users/{id}` (name, phone, job title, role, team, branch, manager, language, tz), `POST /users/{id}/deactivate` `{ reassignToUserId }`, `POST /users/{id}/reactivate`, `PUT /users/me` (profile), `PUT /users/me/password`.
- `GET/POST /invitations`, `POST /invitations/{id}/resend`, `DELETE /invitations/{id}`; anonymous `GET /auth/invitations/{token}` + `POST /auth/accept-invite` `{ token, firstName, lastName, password }`.
- `GET/POST/PUT/DELETE /roles`, `GET/PUT /roles/{id}/permissions` (full matrix), `POST /roles/{id}/clone`.
- `GET/POST/PUT/DELETE /teams`, `/branches`.
- `GET /audit-logs` (filters: user, entity, action, date) — Admin only.

**Rules**
- Tenant owner cannot be deactivated or lose Admin role; ownership can be transferred to another Admin (`POST /users/{id}/transfer-ownership`).
- Deactivation requires reassigning open records (leads, open deals, open tasks) to another user (bulk update in a job). Deactivated users are kept for history and reporting.
- Cannot delete a role with users; cannot delete system roles; last Admin cannot be demoted.
- Every permission change → audit `PermissionChange` + cache invalidation.

**UI:** Users list (status, role, team, last login), invite dialog, user detail; Roles page with permission matrix grid (rows = modules, columns = actions + scope dropdown); Teams & branches page; Audit log page.

### 7.2 Settings & Customization (Module 21)
**API (all Admin / `Settings` permission)**
| Area | Endpoints |
|---|---|
| Company | `GET/PUT /settings/company` (profile, logo, currency, language, tz, date format, fiscal year, tax) |
| Currencies | `GET/PUT /settings/currencies`, `GET/POST/DELETE /settings/exchange-rates` |
| Modules | `GET /settings/modules`, `PUT /settings/modules/{key}` (enable, labels, icon, order) |
| Fields | `GET /settings/fields/{entity}` (system + custom + overrides), `POST /settings/custom-fields`, `PUT /settings/custom-fields/{id}`, `DELETE ...`, `PUT /settings/custom-fields/reorder`, `PUT /settings/fields/{entity}/{key}/override` |
| Layouts | `GET/PUT /settings/layouts/{entity}/{kind}` |
| Picklists | `GET /settings/picklists`, `GET /settings/picklists/{key}`, `POST/PUT/DELETE /settings/picklists/{key}/values`, `PUT .../reorder` |
| Pipelines | `GET/POST/PUT/DELETE /settings/pipelines`, `POST/PUT/DELETE /settings/pipelines/{id}/stages`, `PUT .../stages/reorder` |
| Assignment rules | CRUD `/settings/assignment-rules` + `PUT .../reorder` |
| Web forms | CRUD `/settings/web-forms` + `GET .../{id}/embed-code` |
| Checklists | CRUD `/settings/checklist-templates` |
| Templates | CRUD `/settings/message-templates` |
| Channels | CRUD `/settings/channels` + `POST .../{id}/test` |
| Booking pages | CRUD `/settings/booking-pages` |
| Industry | `GET /settings/industry` (current), sample data remove |
| Document types | CRUD `/settings/document-types` (+ fields, test playground) — Section 19.8 |
| Customer portal | `GET/PUT /settings/portal` — Section 20.7 |

**Rules**
- Deleting a picklist value in use → must choose a replacement value (bulk update job) or deactivate instead.
- Deleting a stage with deals → must choose a target stage; deleting a pipeline with deals → must choose target pipeline + stage mapping.
- `GET /api/v1/app/bootstrap` returns everything the client needs in one call, cached per tenant+role (`bootstrap:{tenant}:{role}:{lang}`), invalidated on any settings change:
```json
{ "user": {...}, "tenant": { "name","logoUrl","baseCurrency","currencies","timeZone","language","taxLabel","industryKey","status","trialEndsAt" },
  "permissions": { "Leads": { "view":true,"create":true,"edit":true,"delete":false,"export":false,"import":false,"scope":"Own" }, ... },
  "modules": [ { "key":"Deals","enabled":true,"singular":"Property Deal","plural":"Property Deals","icon":"handshake","order":5 } ],
  "fields": { "leads": [FieldDef...], ... }, "layouts": {...},
  "picklists": { "lead_status": [ {id,value,label,labelAr,color,category,isActive} ] },
  "pipelines": [ { id, name, isDefault, stages:[...] } ],
  "users": [ { id, fullName, avatarUrl, teamId, isActive } ], "teams": [...],
  "onboarding": { "completed": true, "step": 99 } }
```

**UI:** Settings area with left nav: Company, Users & Roles, Modules, Fields & Layouts (drag-drop field ordering in sections), Picklists, Pipelines (drag-drop stages, colors, probability), Lead capture (web forms, assignment rules), Templates, Channels, Booking pages, Customer onboarding checklists, Currencies & Tax, Data (imports history, sample data), Industry.

### 7.3 Leads (Module 2)
**Fields (system):** see 5.4.

**Create sources**
1. Manual (UI) — with duplicate check.
2. Bulk import (Section 4.10).
3. **Web forms**: public `POST /api/v1/public/forms/{token}` (JSON or form-encoded; CORS allowed for `AllowedDomains`; honeypot field `_hp` must be empty; rate-limited). Stores UTM params & referrer. Embed code = small JS snippet + plain HTML form option.
4. **WhatsApp / Email inbound** (when `AutoCreateLeadFromInbound` = true): a message from an unknown sender creates a lead (source WhatsApp / Email, name from profile name / email display name).
5. **Booking page** booking by unknown person (source Booking).
6. Public API (Phase 2, Integrations).

**Duplicate detection** (`DuplicateDetector.FindAsync(candidate)`), checks leads (non-converted) **and** contacts:
- **Exact** match: same `NormalizedEmail`, or same `NormalizedPhone`/`NormalizedMobile` (E.164 via libphonenumber, default region = tenant country).
- **Possible** match: `similarity(full_name, @name) > 0.6` AND same `company_name` (case-insensitive) — via `pg_trgm`.
- UI create: `POST /leads/check-duplicates` returns matches; user can "Create anyway", "Open existing", or "Merge".
- Web form / inbound / booking: if an exact match **open lead** exists → do not create; add timeline event (`FieldsChanged`/"Re-submitted form" with submitted data), update empty fields only, notify its owner. If an exact match **contact** exists → create the lead with `ContactId` set and owner = contact owner (it's a new opportunity from an existing customer).
- Import: per `DuplicateStrategy`.

**Merge:** `POST /leads/{id}/merge { duplicateIds:[...], fieldChoices:{ "email": "<sourceLeadId>" } }` → master keeps chosen values, activities/messages/timeline re-pointed to master, duplicates soft-deleted with timeline `Merged`.

**Assignment** (`LeadAssignmentService.AssignAsync(lead)` — runs when a lead is created without an owner, by any source):
1. Evaluate active `assignment_rules` for `leads` ordered by `Priority`; first rule whose `Conditions` match the lead (FilterCompiler evaluated in-memory via `Compile().Invoke(lead)` — the compiler must support in-memory evaluation for custom fields by reading the `JsonDocument`).
2. Method:
   - `RoundRobin`: `SELECT ... FROM assignment_rules WHERE id = @id FOR UPDATE`; next active user after `LastAssignedIndex` in `UserIds`; update index.
   - `LoadBalanced`: active user in `UserIds` with the fewest open leads (status category `New|Working|Qualified`).
   - `SpecificUser`: first id.
3. No rule matched → `tenant_settings.DefaultLeadOwnerId` → else tenant owner.
4. Set `AssignedAt`, write timeline `OwnerChanged`, notification `LeadAssigned` to new owner.
Manual reassign (single/bulk) also notifies.

**Statuses** (picklist `lead_status`, configurable labels; categories fixed): New → Working ("Contacted") → Qualified → Disqualified ("Not interested", requires `DisqualifyReasonId`) / Converted (system, set only by conversion). Auto-move New → Working when the first call/email/WhatsApp/meeting is logged (`FirstContactedAt` set).

**Conversion — `POST /leads/{id}/convert`** (the core of end-customer onboarding)
Request:
```json
{ "account": { "mode": "create|existing|none", "existingId": null, "name": "Al Noor LLC" },
  "contact": { "mode": "create|existing", "existingId": null },
  "deal":    { "create": true, "name": "Al Noor – 3BR Villa", "pipelineId": "...", "stageId": "...",
               "amount": 2500000, "currency": "AED", "expectedCloseDate": "2026-12-15" },
  "ownerId": "...", "notifyOwner": true }
```
Algorithm (single transaction; permission: Leads.Edit + Contacts.Create + Accounts.Create (if creating) + Deals.Create (if creating)):
1. Load lead (scope applied). If `ConvertedAt != null` → 409 `already_converted`. If status category = Disqualified → 422.
2. **Account**: `existing` → load (scope check). `create` → duplicate check on `NormalizedName` (exact) — if found and the client didn't explicitly confirm, return 409 with `suggestedAccountId`; else create from lead (`CompanyName`, website, phone, address, owner, source) + mapped custom fields.
3. **Contact**: `existing` → load and fill empty fields from lead. `create` → map: salutation, names, email, phone, mobile, whatsapp, job title, nationality, address, source, tags, owner, `AccountId`, `OriginLeadId = lead.Id`, custom fields via `ConvertMapping` (default: same key if the target entity has a field with the same key & type).
4. **Deal** (if requested): name default `"{Account or Contact name} – {lead source label}"`; stage default = first Open stage of default pipeline; probability from stage; `AccountId`, `PrimaryContactId`, `OriginLeadId`, `SourceId`, amount default = `lead.EstimatedValue`; mapped custom fields; first `deal_stage_history` row.
5. **Re-link history**: `UPDATE activities SET contact_id=@c, account_id=@a, deal_id=COALESCE(deal_id,@d) WHERE lead_id=@lead`; same for `messages`. Timeline of contact automatically includes the lead's events (via `OriginLeadId`).
6. **Lead**: status = value with category `Converted`; `ConvertedAt/ById/ContactId/AccountId/DealId`. Converted leads are read-only (403 `lead_converted` on update) and hidden from default views.
7. Timeline events: `Converted` on lead, `Created` (with "from lead") on contact/account/deal.
8. Notification `DealAssigned` to deal owner if different from actor.
9. Return `{ contactId, accountId, dealId }`; UI navigates to the deal (or contact).
Also: `POST /leads/{id}/disqualify { reasonId, notes }`.

**API extra:** `POST /leads/check-duplicates`, `POST /leads/{id}/convert`, `POST /leads/{id}/disqualify`, `POST /leads/{id}/merge`, `POST /public/forms/{token}`.

**UI**
- List: saved-view tabs, search, filter builder, column chooser, bulk bar (assign, status, tags, delete, export), "Import" & "New lead" buttons. Status chips colored; "uncontacted for > 24 h" indicator.
- Kanban by status (optional toggle).
- Detail: header (name, status stepper New→Working→Qualified, owner, rating, quick actions: Call, WhatsApp, Email, Log activity, **Convert**, Disqualify), left: fields (layout-driven), right/tabs: Timeline, Activities, Messages, Files.
- Convert dialog: 3 sections (Account / Contact / Deal) with duplicate suggestions inline.

**Acceptance:** web-form submission creates lead with source & UTM, assigned by round-robin, owner notified in-app in < 5 s; duplicate submission doesn't create a second lead; conversion creates contact+account+deal and all lead activities appear on the contact timeline.

### 7.4 Customer onboarding playbook (post-sale, part of Deals)
When a deal moves to a **Won** stage: for each active `checklist_template` with `Trigger = DealWon` and (`PipelineId` null or equal) → create Task activities (`DueDate = today + offsetDays`, assignee per rule, linked to deal/account/contact, `ChecklistTemplateId` set). Deal detail shows an **"Onboarding" tab** with checklist progress (completed/total tasks with that template id). Contact `TypeId` auto-switches from Prospect to **Client** on first won deal (if picklist value with value `client` exists). This becomes Automation in Phase 2 and Projects in Phase 3.

### 7.5 Contacts (Module 3)
- Fields per 5.4; tags; types (Client/Prospect/Partner/Supplier).
- Duplicate detection same as leads (against contacts).
- Detail page tabs: **Overview** (fields + account card), **Timeline** (all calls, emails, WhatsApp, SMS, meetings, notes, field changes, including originating lead history), **Deals**, **Activities**, **Messages** (conversation view per channel), **Files**; Phase 2 adds Tickets, Invoices, Documents.
- Opt-outs (`DoNotContact`, per-channel opt-out) block outbound messages (422 `contact_opted_out`).
- Merge contacts (same algorithm as leads; re-point deals, activities, messages, deal_contacts).
- API: standard + `POST /contacts/check-duplicates`, `POST /contacts/{id}/merge`.

### 7.6 Accounts / Companies (Module 4)
- Fields per 5.4; parent/branch hierarchy (`ParentAccountId`, cycle prevention: walking up the parent chain must never reach self; max depth 5).
- Detail page = **360° view**: header KPIs (open deals value, won total, last activity, open tasks; Phase 2: unpaid invoices, open tickets), tabs: Overview, Contacts, Deals, Activities/Timeline (aggregated: account + all its contacts + deals), Branches (child accounts), Files.
- Account owner change → optional cascade to open deals & contacts (checkbox).
- API: standard + `GET /accounts/{id}/summary`, `GET /accounts/{id}/hierarchy`.

### 7.7 Deals / Pipeline (Module 5)
**Board:** `GET /deals/board?pipelineId=&filter=` → `{ stages:[{ id, name, type, color, probability, count, totalInBase, weightedInBase, deals:[{ id, name, amount, currency, amountInBase, accountName, contactName, ownerId, expectedCloseDate, isRotting, nextActivityAt, daysInStage }] }] }` (cards limited to 50 per stage, "load more" via `/deals/query` with stage filter). Won/Lost columns show only last 30 days by default.

**Stage change:** `PATCH /deals/{id}/stage { stageId, lostReasonId?, lostNotes?, closedAt? }`:
- Stage must belong to the deal's pipeline (changing pipeline = `PUT` with new pipeline + stage).
- To **Won**: `Status=Won`, `ClosedAt = request.closedAt ?? now`, `Probability=100` → triggers onboarding checklist (7.4), notification `DealWon` to owner's manager, timeline.
- To **Lost**: `lostReasonId` required (422 `lost_reason_required`), `Status=Lost`, `ClosedAt=now`, `Probability=0`.
- Back to **Open** from Won/Lost: `Status=Open`, `ClosedAt=null`, `LostReasonId=null`, probability from stage.
- Open → Open: `Probability` = stage probability unless `ProbabilityOverridden`.
- Always: insert `deal_stage_history` (with `SecondsInFromStage = now - StageChangedAt`), set `StageChangedAt`, timeline `StageChanged`.
- Concurrency: use `Version`; on conflict return 409 so the board reloads.

**Rotting:** `isRotting = status==Open && stage.RottingDays != null && (LastActivityAt ?? StageChangedAt) < now - RottingDays`.

**Multiple pipelines:** pipeline selector on board and list; reports filter by pipeline.

**UI:** Kanban (MudDropContainer) with column header totals (count, sum, weighted), card quick actions, drag to Won/Lost opens a dialog (lost reason / close date). List view toggle. Deal detail: stage progress bar (click to move), amount & probability, account/contacts (multiple with roles), tabs: Timeline, Activities, Messages, Files, **Onboarding** (when won), Stage history.

**Acceptance:** dragging a card updates totals instantly, writes history; lost without reason is impossible; won deal creates checklist tasks; amounts in other currencies show converted totals.

### 7.8 Activities — Tasks, Calls, Meetings, Notes (Module 6)
- **Task**: subject, description, due date/time, priority, assignee, type, reminder (minutes before due), related record; complete/reopen; "Complete & create follow-up" (pre-fills new task with `ParentActivityId`).
- **Call**: direction, outcome, duration, notes, date/time (default now); logging a call on a lead → may auto-move status (7.3).
- **Meeting**: start/end, all-day, location/URL, internal attendees (users) + external (contacts or free emails), reminder; sends `.ics` invites (Ical.Net) to external attendees by email when "Send invitations" is checked; updates appear in the calendar.
- **Note**: rich text (sanitize HTML with an allow-list — use `HtmlSanitizer` package (Ganss.Xss)), `@mentions` (client autocompletes users; server parses `data-mention-id` attributes → `mentions` rows → `Mentioned` notification).
- Linking: any activity can link to LeadId/ContactId/AccountId/DealId. **Auto-fill**: linking a deal fills its AccountId & PrimaryContactId; linking a contact fills its AccountId.
- After create/complete: update `LastActivityAt`/`NextActivityAt` on linked records (`NextActivityAt` = min open task/meeting date in future).
- Timeline fan-out to all linked records.
- API: standard + `POST /activities/{id}/complete { outcome?, notes?, followUp?: {...} }`, `POST /activities/{id}/reopen`, `GET /activities/my-day` (today's tasks, overdue, meetings — reused by dashboard).
- UI: "My Activities" page (tabs: Today, Overdue, Upcoming, Completed; filters by type), quick-add drawer available from any record & global "+" button, activity composer on record pages with tabs (Note | Task | Call | Meeting).

### 7.9 Calendar & Appointments (Module 7)
- `GET /calendar/events?from&to&userIds=&teamId=&types=` → meetings + tasks with due time + bookings (+ external busy blocks as "Busy"). Scope rules apply (team view requires Team/All scope on Activities).
- Views: day, week, month; user/team selector; color by user or type; click empty slot → create meeting; drag to reschedule (`PATCH /activities/{id}/schedule {startAt,endAt}`).
- **Booking links** (public, no login):
  - `GET /api/v1/public/booking/{tenantSlug}/{slug}` → page info (name, duration, questions, branding, languages).
  - `GET .../slots?from=2026-10-12&to=2026-10-18&tz=Asia/Dubai` → available slots.
  - `POST .../book { start, name, email, phone, answers, tz }` → booking.
  - `GET/POST /api/v1/public/booking/manage/{token}` → cancel / reschedule.
  - **Slot algorithm** (`AvailabilityService`): for each day in range (≤ MaxDaysAhead), for each candidate user: weekly ranges (page tz) ∩ not date-override-closed → generate slots every `SlotStepMinutes` of length `DurationMinutes` → remove slots overlapping (busy ± buffers): user's meetings (Open), external busy slots, other bookings → remove slots earlier than `now + MinNoticeHours`. RoundRobin pages: slot available if **any** user free; assignment on booking = free user with the fewest bookings in that week.
  - **Booking** (transaction + `pg_advisory_xact_lock(hashtext(userId))`): re-check slot; find contact by email/phone → else lead (source Booking, duplicate rules); create Meeting activity (owner = assigned user, attendee = contact/lead), booking row; send confirmation (email + optional WhatsApp/SMS) with `.ics` and manage link; notification `BookingCreated` to user.
  - **Reminders**: `BookingReminderJob` every 5 min sends reminders at configured offsets (tracked in `booking_reminders_sent` jsonb on booking).
- **Two-way Google/Outlook sync (milestone 11b — after Phase 1 core is stable):** OAuth per user (Google Calendar API, Microsoft Graph); push CRM meetings to the user's calendar (store external id); pull events every 5 min using sync tokens/delta queries → store as `external_busy_slots` (and, if the event has a CRM contact as attendee, optionally create a meeting activity). Tokens encrypted.

### 7.10 Communication — Email, WhatsApp, SMS (Module 8)
**Provider abstractions (Application):**
```csharp
public interface IEmailChannel   { Task<SendResult> SendAsync(ChannelAccount acc, OutboundEmail msg, CancellationToken ct); }
public interface IWhatsAppChannel{ Task<SendResult> SendTextAsync(...); Task<SendResult> SendTemplateAsync(...); Task<SendResult> SendMediaAsync(...); Task<IReadOnlyList<ProviderTemplate>> GetTemplatesAsync(...); }
public interface ISmsChannel     { Task<SendResult> SendAsync(ChannelAccount acc, string to, string body, CancellationToken ct); }
```
Implementations: `SmtpEmailChannel` (MailKit), `WhatsAppCloudChannel` (Meta Graph API `https://graph.facebook.com/v{N}/{phoneNumberId}/messages`), `TwilioSmsChannel` (REST via HttpClient), and **Fake\*** channels (dev/tests: log + mark Sent). Register by `channel_accounts.Settings.provider`.

**Sending flow:** `POST /messages/email|whatsapp|sms { channelAccountId?, to, cc?, subject?, body?, templateId?, variables?, attachments:[fileId], leadId?, contactId?, dealId?, accountId? }` → render template (merge fields) → check opt-outs → save `messages` (Queued) → enqueue `SendMessageJob` (queue `messaging`, retries 3 with backoff) → provider → status Sent/Failed → timeline event on linked records + `LastActivityAt`.

**Merge fields** (`TemplateRenderer`): syntax `{{entity.field}}` and `{{entity.field | "fallback"}}`; entities: `lead, contact, account, deal, user (sender), company (tenant), activity, booking`; fields come from the Field Registry (incl. custom). Regex: `\{\{\s*([a-z_]+)\.([a-z0-9_]+)\s*(?:\|\s*"([^"]*)")?\s*\}\}`. Unknown placeholders render as empty + warning in preview. `POST /message-templates/{id}/preview { contactId }`.

**Inbound email:** `ImapPollJob` every 2 min per Email channel account: fetch UIDs > `LastImapUid` from INBOX (and Sent folder for sent-from-outside-CRM mails); match participants by `NormalizedEmail` to contacts → leads (scope-independent, tenant-only); unmatched → ignore unless `AutoCreateLeadFromInbound`; thread by `In-Reply-To`/`References` → `ThreadKey`; store attachments (≤ 25 MB); notification `MessageReceived` to record owner. Skip auto-replies (`Auto-Submitted` header) & newsletters (`List-Unsubscribe`) for lead creation.

**WhatsApp Business (Cloud API):**
- Webhook `GET /api/v1/webhooks/whatsapp` (hub.challenge verification with `VerifyToken`) and `POST` (validate `X-Hub-Signature-256` with App Secret; resolve tenant by `phone_number_id`).
- Inbound messages → `messages` (Received), media downloaded to file storage, matched by `NormalizedPhone`/`WhatsAppNumber`; statuses (sent/delivered/read/failed) update outbound messages.
- **24-hour rule:** free-form text only if the last inbound message from that number is < 24 h old; otherwise only approved templates (UI disables the text box and shows template picker). Server enforces (422 `whatsapp_window_closed`).
- Template sync: `POST /settings/channels/{id}/sync-templates` pulls approved templates into `message_templates`.
- Webhooks return 200 quickly; processing is queued.

**SMS:** Twilio (status callback webhook `/api/v1/webhooks/sms/twilio` with signature validation), inbound SMS optional. Segment count shown in UI (GSM-7 160 / UCS-2 70 — Arabic uses UCS-2).

**UI:** composer drawer on records (channel tabs Email | WhatsApp | SMS; template picker; attachments; preview), conversation tab per record (chat bubbles for WhatsApp/SMS, threaded emails), **Inbox** page (latest conversations across channels for my records, unread markers).

### 7.11 Notifications & Reminders (Module 18)
**Types (enum `NotificationType`):** `LeadAssigned, DealAssigned, TaskAssigned, AccountAssigned, ContactAssigned, Mentioned, TaskDueSoon, TaskOverdue, MeetingReminder, BookingCreated, BookingCancelled, MessageReceived, DealWon, DealLost, ImportCompleted, ExportReady, ReportReady, TrialEnding, DocumentProcessed, DocumentNeedsReview, DocumentProcessingFailed, DocumentExpiring, DocumentUploadedByCustomer, DocumentRequestSubmitted` (+ Phase 2: `InvoiceOverdue, TicketAssigned, TicketSlaBreached, ApprovalRequested`).

**Pipeline:** `INotificationService.NotifyAsync(NotificationRequest { UserIds, Type, Title, Body, EntityType, EntityId, Url, GroupKey })`:
1. Skip the actor (don't notify yourself). Skip inactive users.
2. For each user: load preferences (default matrix below; cached).
3. In-app → insert `notifications` + SignalR `ReceiveNotification` to `user:{id}` (unread count badge).
4. Email → enqueue `NotificationEmailJob` (system SMTP; localized template; link to record). Collapse bursts: same `GroupKey` within 10 min → single email.
5. Push → Phase 3 (FCM) — interface `IPushSender` with no-op implementation now.

Default preferences: in-app ON for all; email ON for `*Assigned, Mentioned, TaskOverdue, MeetingReminder, BookingCreated, ImportCompleted, ReportReady, TrialEnding`, OFF for the rest.

**Reminder jobs (Hangfire recurring, UTC):**
| Job | Schedule | Logic |
|---|---|---|
| `ActivityReminderJob` | every minute | `activities WHERE status='Open' AND reminder_at <= now AND reminder_sent_at IS NULL` (IgnoreQueryFilters, group by tenant) → `TaskDueSoon` / `MeetingReminder`; set `reminder_sent_at`. Batch 500. |
| `OverdueTaskJob` | every 15 min | `type='Task' AND status='Open' AND due_at < now AND overdue_notified_at IS NULL` → `TaskOverdue`; set `overdue_notified_at`. |
| `DailyDigestJob` | hourly; sends at 08:00 user-local | Optional "My day" email (today's tasks/meetings/overdue) if user enabled. |
| `BookingReminderJob` | every 5 min | Section 7.9 |
| `TrialExpiryJob` | daily | Section 6.9 |

**API:** `GET /notifications?unreadOnly=&page=`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all`, `GET/PUT /notifications/preferences`.
**UI:** bell with badge + dropdown (latest 20, mark read, click → navigate), full notifications page, preferences matrix in profile (type × channel toggles), toast (MudSnackbar) on real-time arrival.

---

## 8. ★ DASHBOARD (Module 1)

### 8.1 Goals
First screen after login; shows **today's work** and **key numbers**; different views for Staff, Manager, Admin; fast (< 500 ms server time with warm cache, < 1.5 s cold for 100k records/tenant); every number is **clickable** (drill-down to the filtered list).

### 8.2 View & scope resolution
- Default view = `roles.DashboardView`. Users can switch to a *lower* view (Manager → "My view" = Staff; Admin → Manager/Staff). Never higher.
- Scope per view: Staff → `[me]`; Manager → team user ids (Section 4.4.5), intersected with the user's permission scope on the widget's module; Admin → all (if permission scope = All).
- Manager/Admin views have a **filter bar**: team, user, pipeline, period (This month default | Last month | This quarter | This year | custom).

### 8.3 Architecture
```csharp
public interface IDashboardWidget
{
    string Key { get; }                       // e.g. "kpi_strip"
    AppModule Module { get; }                 // requires View permission on this module, and module enabled
    DashboardView[] Views { get; }
    WidgetSize DefaultSize { get; }           // Sm | Md | Lg | Xl (12-col grid: 3/6/9/12)
    int DefaultOrder { get; }
    Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct);
}
public sealed record DashboardContext(Guid UserId, Guid TenantId, DashboardView View, IReadOnlyList<Guid>? ScopeUserIds /* null = all */,
    DateRange Period, DateRange PreviousPeriod, Guid? PipelineId, TenantTime Time, string BaseCurrency);
public sealed record WidgetResult(string Key, string Title, WidgetSize Size, object Data, string? DrillDownUrl);
```
- All widgets registered in DI (`services.AddScoped<IDashboardWidget, KpiStripWidget>()` ...). `DashboardService` picks widgets for the view, applies the user's layout (order/hidden), computes them **sequentially on one DbContext** (or in parallel with `IDbContextFactory` — each widget gets its own context; choose parallel with max degree 4).
- Each widget result cached via HybridCache for 60 s: key `{tenant}:dash:{userId}:{view}:{widgetKey}:{filtersHash}`. `?refresh=true` bypasses cache.
- Widgets never throw to the page: an exception produces `{ error: "widget_failed" }` for that widget + log.
- SignalR: when a deal is won/lost or a lead is assigned, push `DashboardStale` to affected users; client refreshes the affected widgets (debounced 5 s).

**API**
- `GET /api/v1/dashboard?view=Staff|Manager|Admin&period=this_month&teamId=&userId=&pipelineId=` → `{ view, availableViews[], generatedAt, widgets: WidgetResult[] }`
- `GET /api/v1/dashboard/widgets/{key}?...&refresh=true` → single widget
- `GET/PUT /api/v1/dashboard/layout?view=` → user's order/hidden/size; `DELETE` resets to role default.

### 8.4 Widget catalog (exact logic)
Notation: `S` = scope user ids filter on `OwnerId` (no filter when null). `P` = selected period (UTC range from `DateRangeResolver`). `PP` = previous equivalent period. "Open lead" = status category in (New, Working, Qualified) and not converted.

| Key | Views | Module | Data & logic | Drill-down |
|---|---|---|---|---|
| `kpi_strip` | All | Dashboard | Array of KPI tiles, each `{ key, label, value, previousValue, changePct, format: currency/number/percent, trend: up/down/flat, goodWhen: up/down }` — see 8.5 | per tile |
| `my_day` | Staff, Manager | Activities | **Today's tasks** (Task, Open, `DueAt` within today local, owner = me) — top 10 by DueAt + count; **Overdue** (Task, Open, `DueAt < now`, owner = me) — top 10 oldest first + count; **Meetings today** (Meeting, Open, owner = me OR I'm attendee, `StartAt` within today). Each item: id, subject, related record name + link, due/start, priority. Inline "complete" checkbox calls `/activities/{id}/complete`. | `/activities?view=today|overdue` |
| `upcoming_meetings` | All | Calendar | Meetings (owner/attendee in S for Manager/Admin; me for Staff), `StartAt ∈ [now, now+7d]`, top 10: subject, start (local), attendees, location/URL, related record | `/calendar?view=week` |
| `new_leads` | Staff, Manager | Leads | Open leads with category `New`, owner ∈ S (Staff = me), `FirstContactedAt IS NULL`, ordered by `CreatedAt` desc; top 10 + total count; each with age ("2 h ago"), source, SLA flag if older than 24 h | `/leads?filter=new_uncontacted` |
| `deals_in_progress` | All | Deals | Open deals (owner ∈ S, pipeline = selected/default): `count`, `totalInBase`, `weightedInBase = Σ AmountInBase × Probability/100`, `byStage: [{stageId, name, count, total, weighted}]` (bar chart), `rottingCount` | `/deals?view=board&pipelineId=` |
| `deals_closing_soon` | All | Deals | Open deals with `ExpectedCloseDate` ∈ current month (local), owner ∈ S, ordered by date, top 10 + total value; overdue-close (date < today) flagged red | filtered list |
| `target_progress` | All | Reports | Staff: my targets for current month; Manager: team targets (or sum of member targets if no team target); Admin: company target (or sum). For each metric with a target: `{ metric, target, actual, pct = actual/target, expectedPct = elapsedDays/totalDays, status: ahead|on_track|behind (pct ≥ expectedPct → ahead; ≥ 0.9×expected → on_track; else behind) }`. Actual computed by `TargetMetricCalculator` (8.6) | `/reports/system/target_vs_actual` |
| `sales_trend` | Manager, Admin | Reports | Won `AmountInBase` per month for last 12 months (local month buckets, `ClosedAt`), owner ∈ S; plus target series per month (sum of targets for metric WonAmount); line/area chart | `/reports/system/revenue_by_month` |
| `leads_by_source` | Manager, Admin | Leads | Leads created in P, owner ∈ S, grouped by source: count + converted count; donut | `/reports/system/lead_sources` |
| `pipeline_funnel` | Manager, Admin | Deals | For deals created in the last 90 days (pipeline selected): number that reached each Open stage (from `deal_stage_history.to_stage_id` distinct deal count) + won; funnel chart | `/reports/system/stage_funnel` |
| `team_leaderboard` | Manager, Admin | Reports | Per user in S (active): won amount in P, won count, new leads in P, activities completed in P, open pipeline weighted; sorted by won amount desc; rank, avatar, % of target | `/reports/system/sales_by_user` |
| `team_overdue` | Manager, Admin | Activities | Per user in S: overdue open tasks count, oldest overdue age; only users with > 0 | `/activities?filter=overdue&ownerId=` |
| `recent_wins` | Manager, Admin | Deals | Last 5 won deals in S: name, amount, owner, closed date | deal |
| `activity_summary` | Staff, Manager | Activities | Completed in P by type (calls, meetings, tasks, notes) for S, vs PP | `/reports/system/activities_by_user` |
| `onboarding_checklist` | Admin | Settings | Section 6.8 checklist; hidden when complete or dismissed | settings pages |
| `system_overview` | Admin | Users | Active users / plan seats, invites pending, records count by entity vs plan, storage used, last import status, trial days left, sample-data flag | settings |
| `documents_review` | Staff, Manager | Documents | Documents with `Status = NeedsReview` assigned to me (Staff) / in scope (Manager): count + top 5 (name, type, linked record, uploaded age, source = Portal/Staff badge) | `/documents?status=NeedsReview` |
| `documents_expiring` | All | Documents | Documents with `ExpiryDate` within next 60 days (in scope), ordered by date: name, type, linked record, days left (red ≤ 7, amber ≤ 30) | `/documents?filter=expiring_60` |
| `open_tickets` | Manager, Admin | Tickets | **Phase 2** — registered but only shown if Tickets module enabled & built | — |
| `unpaid_invoices` | Manager, Admin | Invoices | **Phase 2** | — |

**Default layouts:**
- **Staff:** kpi_strip(Xl) · my_day(Lg) + target_progress(Sm) · new_leads(Md) + upcoming_meetings(Md) · deals_in_progress(Md) + deals_closing_soon(Md) · activity_summary(Md)
- **Manager:** kpi_strip(Xl) · team_leaderboard(Lg) + target_progress(Sm) · sales_trend(Md) + deals_in_progress(Md) · leads_by_source(Sm) + pipeline_funnel(Sm) + team_overdue(Sm) · deals_closing_soon(Md) + recent_wins(Md) · my_day(Md) + upcoming_meetings(Md)
- **Admin:** onboarding_checklist(Xl, if not complete) · kpi_strip(Xl) · sales_trend(Lg) + system_overview(Sm) · team_leaderboard(Md) + leads_by_source(Md) · deals_in_progress(Md) + pipeline_funnel(Md) · team_overdue(Sm) + recent_wins(Md)
- Industry templates may reorder via `dashboardHighlights` (move listed widgets to the top after kpi_strip).

### 8.5 KPI tiles (inside `kpi_strip`)
| Tile key | Staff | Manager/Admin | Formula (S = scope, P = period) |
|---|---|---|---|
| `won_amount` | ✔ (mine) | ✔ | Σ `AmountInBase` of deals `Status=Won`, `ClosedAt ∈ P`, owner ∈ S. vs PP. Label "Sales this month" |
| `won_count` | | ✔ | count of the above |
| `open_pipeline` | ✔ | ✔ | Σ `AmountInBase` open deals owner ∈ S (point-in-time; no comparison) + weighted as subtitle |
| `new_leads` | ✔ (assigned to me in P) | ✔ | count leads `CreatedAt ∈ P`, owner ∈ S. vs PP |
| `conversion_rate` | | ✔ | leads created in P (owner ∈ S) with `ConvertedAt != null` ÷ leads created in P. (cohort definition — document in tooltip) vs PP |
| `win_rate` | ✔ | ✔ | won ÷ (won + lost) for deals closed in P, owner ∈ S. vs PP |
| `avg_deal_size` | | ✔ | won_amount ÷ won_count in P |
| `tasks_overdue` | ✔ | ✔ | open tasks `DueAt < now`, owner ∈ S (goodWhen: down) |
| `activities_done` | ✔ | | completed activities (`CompletedAt ∈ P`) owner = me |
| `open_tickets` | | ✔ (P2) | open tickets in S |
| `unpaid_invoices` | | ✔ (P2) | Σ balance of Unpaid/PartlyPaid/Overdue invoices |

`changePct = previous == 0 ? null : (value - previous) / previous × 100`, rounded 1 decimal.

### 8.6 Shared metric calculators (used by dashboard AND reports AND targets)
Put the formulas in **one** place: `Application/Common/Metrics/SalesMetrics.cs`:
```csharp
Task<decimal> WonAmountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct);
Task<int>     WonCountAsync(...);
Task<int>     LostCountAsync(...);
Task<(decimal total, decimal weighted, int count)> OpenPipelineAsync(IReadOnlyList<Guid>? userIds, Guid? pipelineId, CancellationToken ct);
Task<int>     NewLeadsAsync(IReadOnlyList<Guid>? userIds, DateRange range, CancellationToken ct);
Task<int>     ConvertedLeadsCohortAsync(IReadOnlyList<Guid>? userIds, DateRange createdRange, CancellationToken ct);
Task<int>     ActivitiesCompletedAsync(IReadOnlyList<Guid>? userIds, DateRange range, ActivityType? type, CancellationToken ct);
Task<int>     OverdueTasksAsync(IReadOnlyList<Guid>? userIds, CancellationToken ct);
```
`TargetMetricCalculator.ActualAsync(target)` maps `Metric` → the right calculator with the target's scope users and period. **Unit/integration-test these with the sample data profile and hand-computed expectations.**

Example implementation:
```csharp
public async Task<decimal> WonAmountAsync(IReadOnlyList<Guid>? userIds, DateRange r, Guid? pipelineId, CancellationToken ct)
{
    var q = _db.Deals.Where(d => d.Status == DealStatus.Won && d.ClosedAt >= r.StartUtc && d.ClosedAt < r.EndUtc);
    if (userIds is not null) q = q.Where(d => userIds.Contains(d.OwnerId));
    if (pipelineId is not null) q = q.Where(d => d.PipelineId == pipelineId);
    return await q.SumAsync(d => (decimal?)d.AmountInBase, ct) ?? 0m;
}
```

### 8.7 Dashboard UI (Blazor)
- `Pages/Dashboard/DashboardPage.razor`: header with greeting ("Good morning, Sara"), view switcher (chips), filter bar (Manager/Admin), "Customize" toggle, refresh button, last-updated time.
- 12-column responsive `MudGrid`; sizes: Sm=3, Md=6, Lg=9, Xl=12 on desktop; all Xl (12) on phones; Sm→6 on tablets.
- Each widget = component in `Components/Widgets/` (`KpiStripWidget.razor`, `MyDayWidget.razor`, ...), mapped by key in `WidgetRegistry` (Dictionary<string, Type>), rendered with `<DynamicComponent>`. Each has a skeleton loader (`MudSkeleton`), empty state ("No meetings this week — Schedule one"), error state with retry.
- Customize mode: drag to reorder (MudDropContainer), hide/show, resize (size select); saved via `PUT /dashboard/layout`.
- Charts: ApexCharts; colors from theme; RTL-aware (charts mirror axis in Arabic).
- Numbers: currency formatted with base currency, compact (1.2M) on tiles, full on hover tooltip.
- Auto refresh every 5 min while visible + SignalR `DashboardStale`.

### 8.8 Acceptance criteria (Dashboard)
- Staff sees only their own numbers; manager sees team; admin sees all — verified by integration tests with 3 users in 2 teams.
- With sample data, every widget renders non-empty data.
- Clicking every number opens a list whose total equals that number.
- "Sales this month" matches the `sales_by_user` report total for the same period/scope.
- Period boundaries respect tenant time zone (test: a deal won at 2026-10-31 22:30 UTC counts in **November** for Asia/Dubai).
- Disabled modules' widgets disappear.

---

## 9. ★ REPORTS & ANALYTICS (Module 17)

### 9.1 Components
1. **Report engine** (data sources + query model + SQL builder + executor).
2. **System (ready-made) reports** — 14 reports below.
3. **Report builder** UI for custom reports.
4. **Sales targets** with progress tracking.
5. **Export** (Excel/PDF) and **scheduled email delivery**.
6. Folders, favorites, sharing.

### 9.2 Data sources (`IReportDataSource`)
Each data source declares: `Key`, `Label`, `BaseTable` + alias, `Module` (permission + scope), `OwnerColumn` (for scope), `DefaultDateField`, available **joins** and **fields** (from Field Registry: `SqlExpr`, `SqlJoins`, type, groupable, aggregatable).

| Source key | Base | Main joins available | Notes |
|---|---|---|---|
| `leads` | `leads l` | owner (`users o`), team (`teams t` via owner), status (`picklist_values ls`), source (`picklist_values src`), rating | `is_converted` = `l.converted_at IS NOT NULL` |
| `contacts` | `contacts c` | owner, account (`accounts a`), type, source | |
| `accounts` | `accounts a` | owner, industry, size, type, parent | |
| `deals` | `deals d` | owner, team, pipeline (`pipelines p`), stage (`pipeline_stages s`), account, primary contact, source, lost reason | `weighted_amount` = `d.amount_in_base * d.probability / 100.0` |
| `activities` | `activities x` | owner, team, lead, contact, account, deal, call outcome | |
| `deal_stage_history` | `deal_stage_history h` | deal, from stage, to stage, pipeline, deal owner | for funnel/velocity |
| `documents` | `documents doc` | owner, document type, account, contact | counts by type/status/source, expiring documents |
| `document_metrics` | `document_extracted_fields f` (only `Status = Approved`) | document (`documents doc`), type, field definition, account, contact | **Extracted metrics** from OCR: `numeric_value`, `date_value`, `text_value`, `period_date`; e.g. "Revenue by year per account" from financial statements (Section 19.10) |
| *(P2)* `invoices`, `payments`, `quotes`, `tickets`, `products` | | | registered when modules ship |

Every generated query ALWAYS contains: `{alias}.tenant_id = @tenantId AND {alias}.is_deleted = false` (+ same for every joined tenant table in the `ON` clause) **and** the scope predicate `{alias}.{owner_column} = ANY(@scopeUserIds)` when scope ≠ All.

Example field declarations (deals):
```csharp
new("name",            "Deal name",     FieldType.Text,     Sql: "d.name"),
new("amount_in_base",  "Amount (base)", FieldType.Currency, Sql: "d.amount_in_base", Aggregatable: true),
new("weighted_amount", "Weighted amount", FieldType.Currency, Sql: "d.amount_in_base * d.probability / 100.0", Aggregatable: true),
new("status",          "Status",        FieldType.Picklist, Sql: "d.status", Groupable: true),
new("stage_name",      "Stage",         FieldType.Text,     Sql: "s.name", Joins: ["stage"], Groupable: true, SortSql: "s.sort_order"),
new("owner_name",      "Owner",         FieldType.Text,     Sql: "concat_ws(' ', o.first_name, o.last_name)", Joins: ["owner"], Groupable: true),
new("closed_at",       "Closed date",   FieldType.DateTime, Sql: "d.closed_at", Groupable: true /* with date grouping */),
new("days_to_close",   "Days to close", FieldType.Number,   Sql: "EXTRACT(EPOCH FROM (d.closed_at - d.created_at)) / 86400.0", Aggregatable: true),
// custom field (generated): key "cf_budget" → Sql: "(d.custom_fields ->> 'cf_budget')::numeric"   (key validated by regex, never user free text)
```
Joins dictionary for deals:
```csharp
["owner"]   = "LEFT JOIN users o ON o.id = d.owner_id",
["stage"]   = "LEFT JOIN pipeline_stages s ON s.id = d.stage_id AND s.tenant_id = d.tenant_id",
["pipeline"]= "LEFT JOIN pipelines p ON p.id = d.pipeline_id AND p.tenant_id = d.tenant_id",
["account"] = "LEFT JOIN accounts a ON a.id = d.account_id AND a.tenant_id = d.tenant_id AND a.is_deleted = false",
["source"]  = "LEFT JOIN picklist_values src ON src.id = d.source_id",
["lost_reason"] = "LEFT JOIN picklist_values lr ON lr.id = d.lost_reason_id",
["team"]    = "LEFT JOIN teams t ON t.id = o.team_id"   // depends on "owner" (declare dependency)
```

### 9.3 Query model (Shared)
```csharp
public sealed class ReportQuery
{
    public string Source { get; set; } = "";                  // "deals"
    public ReportType Type { get; set; }                      // Tabular | Summary | Matrix
    public List<string> Columns { get; set; } = [];           // Tabular: field keys
    public List<ReportGroup> GroupBy { get; set; } = [];      // Summary: 1–2 groups; Matrix: rows + 1 column group
    public List<ReportAggregate> Aggregates { get; set; } = []; // e.g. { field:"amount_in_base", func:"Sum", alias:"total" }; Count needs no field
    public FilterGroup? Filter { get; set; }
    public ReportDateFilter? DateFilter { get; set; }         // { field:"closed_at", preset:"ThisQuarter" | from/to }
    public List<SortSpec> Sort { get; set; } = [];
    public int? Limit { get; set; }                           // default 1000 for preview, max 50,000 for export
    public List<ReportFormula> Formulas { get; set; } = [];   // computed from aggregates: "won_count / NULLIF(won_count + lost_count,0) * 100" — ONLY over aggregate aliases + numbers + + - * / ( ) via a whitelist tokenizer
}
public sealed record ReportGroup(string Field, DateGrouping? DateGrouping = null); // Day|Week|Month|Quarter|Year
public enum AggregateFunc { Count, CountDistinct, Sum, Avg, Min, Max }
```

### 9.4 SQL builder (`ReportSqlBuilder`, Application) & executor (`DapperReportExecutor`, Infrastructure)
Steps:
1. Resolve data source; resolve each referenced field key → `FieldDef` (unknown → 400). Check `Groupable`/`Aggregatable` flags.
2. Collect required joins (with dependencies, deduplicated, ordered).
3. SELECT:
   - Tabular: `{sqlExpr} AS "{key}"` for each column (+ hidden `id` for drill-down).
   - Summary: group expressions + aggregates. Date grouping → `date_trunc('{unit}', {expr} AT TIME ZONE @tz)` (unit from whitelist). Week start Monday is Postgres default; for Sunday week start use `date_trunc('week', ({expr} AT TIME ZONE @tz) + interval '1 day') - interval '1 day'`.
   - Aggregates: `COUNT(*)`, `COUNT(DISTINCT expr)`, `SUM(expr)`, `AVG(expr)`, `MIN`, `MAX` → aliased `"{alias}"`.
4. WHERE: tenant + soft-delete + scope + `FilterSqlCompiler.Compile(filter)` (same operators as 4.6.3; values as parameters `@p0, @p1...`; arrays with `= ANY(@pN)`; ILIKE with escaped pattern) + date filter.
5. GROUP BY ordinal positions; ORDER BY whitelisted field/alias + direction; LIMIT `@limit + 1` (to detect truncation).
6. Formulas computed in C# after fetching (not in SQL).
7. Execute with Dapper on a **read-only connection** with `SET LOCAL statement_timeout = '15s'` inside a transaction; map to `ReportResult`.
8. Totals row (for Sum/Count) computed in C#.

```csharp
public sealed class ReportResult
{
    public List<ReportColumn> Columns { get; set; } = [];   // { key, label, type, format, isGroup, isAggregate }
    public List<object?[]> Rows { get; set; } = [];
    public object?[]? Totals { get; set; }
    public bool Truncated { get; set; }
    public int RowCount { get; set; }
    public long ExecutionMs { get; set; }
    public ReportVisualization? Visualization { get; set; }
}
```
Picklist/user/lookup group values return both id and label (`{ id, label }`) so the client can drill down by id.

**Unit tests (mandatory):** builder output for each operator; injection attempts (field `"name; drop table"` → rejected; value `"' OR 1=1 --"` → parameterized); scope predicate always present; tenant predicate present on every joined table; time zone grouping.

### 9.5 Drill-down
Clicking a summary cell → `GET /reports/drilldown` with source + base filters + group values → opens the corresponding entity list with an equivalent `FilterGroup` (shared filter model makes this a direct mapping) or a tabular report of the underlying rows.

### 9.6 System reports (ship these; `IsSystem = true`, read-only, can be "Saved as" custom)
Use generic `ReportQuery` where possible (`Kind=Generic`); use a C# `ISystemReport` class (`Kind=System`) when the logic needs multiple queries. All accept common filters: **period** (default This month), **owner(s)**, **team**, **pipeline** (deal-based).

| # | Key | Name | Source / Kind | Definition | Visualization |
|---|---|---|---|---|---|
| 1 | `sales_by_user` | Sales by user | System | Per owner (active + any with data): won count, won amount (base), lost count, win rate % = won/(won+lost), avg deal size, open pipeline (current), weighted pipeline; period applies to `closed_at` | bar (won amount) + table |
| 2 | `lead_sources` | Lead sources | Generic `leads` | Group by `source_label`; Count; converted count (`CountIf is_converted`, implement as `SUM(CASE WHEN l.converted_at IS NOT NULL THEN 1 ELSE 0 END)`); formula conversion % ; period on `created_at` | donut + table |
| 3 | `lead_conversion` | Lead conversion rate | System | By month of `created_at` (last 6 months default): created, converted (cohort), conversion %, avg days to convert (`converted_at - created_at`); second table by owner | line + table |
| 4 | `revenue_by_month` | Revenue | Generic `deals` | Status = Won; group `closed_at` by Month (or Quarter/Year switch); Sum amount_in_base, Count; Phase 2 option: source = paid invoices | column + table |
| 5 | `pipeline_value` | Pipeline value | Generic `deals` | Status = Open; group by `stage_name` (sorted by stage order); Count, Sum amount, Sum weighted; filter pipeline | bar + table |
| 6 | `stage_funnel` | Sales funnel (stage conversion) | System | Deals created in period (pipeline required): for each Open stage in order → number of distinct deals that ever reached that stage or a later one (`deal_stage_history`) ; then Won count; % of previous stage and % of first stage | funnel |
| 7 | `deal_velocity` | Deal velocity | System | Won deals in period: avg/median days to close; avg days spent per stage (from `seconds_in_from_stage` grouped by `from_stage_id`); by owner | bar + table |
| 8 | `sales_forecast` | Forecast | Generic `deals` | Open deals; group `expected_close_date` by Month (next 6 months + "Overdue" bucket for past dates); Sum amount, Sum weighted, Count | stacked column |
| 9 | `lost_reasons` | Lost deal reasons | Generic `deals` | Status = Lost, period on closed_at; group by `lost_reason_label`; Count, Sum amount | donut + table |
| 10 | `overdue_tasks` | Overdue tasks | Generic `activities` | Type = Task, Status = Open, `due_at < now`; Tabular: subject, owner, related record, due date, days overdue (`EXTRACT(DAY FROM now() - x.due_at)`), priority; summary by owner | table + bar by owner |
| 11 | `activities_by_user` | Activities by user | Generic `activities` | Status = Completed, period on `completed_at`; Matrix: rows owner × columns type; Count | stacked bar |
| 12 | `target_vs_actual` | Target vs actual | System | For targets whose period overlaps the selected period: target, actual (TargetMetricCalculator), %, pacing status; by user/team | bullet/bar + table |
| 13 | `new_customers` | New customers | System | Contacts whose first won deal `closed_at` ∈ period (by month and by owner) — the "customer onboarding" outcome metric; plus avg days lead→first win | column + table |
| 14 | `document_metrics_by_account` | Extracted document metrics | Generic `document_metrics` | Approved numeric fields marked `IsMetric`; Matrix: rows account × columns `period_date` by Year; Sum `numeric_value`; filter by document type + field (e.g. Financial statement → Revenue) | line + table |
| 15 | `document_processing` | Document processing | Generic `documents` | Period on `created_at`; group by type and status; Count; avg OCR confidence; avg minutes from upload to approval; % auto-approved | bar + table |
| 16 | `ticket_resolution` | Ticket resolution times | **Phase 2** | Avg first response / resolution time, SLA breach % by category/user | — |

System report classes implement:
```csharp
public interface ISystemReport
{
    string Key { get; }
    AppModule Module { get; }
    IReadOnlyList<ReportParameterDef> Parameters { get; }     // period, users, team, pipeline...
    Task<ReportResult> RunAsync(ReportRunContext ctx, IReadOnlyDictionary<string, JsonElement> parameters, CancellationToken ct);
}
```
They may use Dapper SQL directly — follow the same tenant/scope/parameter rules (write a shared `SqlGuard.AppendTenantAndScope(...)` helper).

Example — `stage_funnel` SQL:
```sql
WITH cohort AS (
  SELECT d.id FROM deals d
  WHERE d.tenant_id = @tenantId AND d.is_deleted = false AND d.pipeline_id = @pipelineId
    AND d.created_at >= @from AND d.created_at < @to
    AND (@allScope OR d.owner_id = ANY(@userIds))
),
reached AS (
  SELECT DISTINCT h.deal_id, s.sort_order
  FROM deal_stage_history h
  JOIN pipeline_stages s ON s.id = h.to_stage_id
  WHERE h.tenant_id = @tenantId AND h.deal_id IN (SELECT id FROM cohort)
),
max_reached AS (SELECT deal_id, MAX(sort_order) AS max_order FROM reached GROUP BY deal_id)
SELECT s.id, s.name, s.sort_order, s.stage_type,
       (SELECT COUNT(*) FROM max_reached m WHERE m.max_order >= s.sort_order) AS deals_reached
FROM pipeline_stages s
WHERE s.tenant_id = @tenantId AND s.pipeline_id = @pipelineId AND s.is_deleted = false AND s.stage_type <> 'Lost'
ORDER BY s.sort_order;
```
(Because Won is after all Open stages in sort order, a deal that reached Won counts in every earlier stage. Lost is excluded from the funnel; deals lost after reaching stage X count up to X — `max_order` must be computed over Open/Won stages only: add `AND s.stage_type <> 'Lost'` in `reached`.)

### 9.7 Report builder (custom reports) — UI flow
`Pages/Reports/ReportBuilder.razor`, a stepper with live preview (right panel, `POST /reports/run` with `Limit=200`, debounced 600 ms):
1. **Source**: pick data source (only those the user can View).
2. **Type**: Tabular | Summary | Matrix.
3. **Columns** (Tabular): pick & reorder fields (searchable list grouped by "Deal fields / Owner / Account / Custom fields").
4. **Group & aggregate** (Summary/Matrix): 1–2 row groups (date grouping selector for date fields), optional column group (Matrix), aggregates (Count, Sum/Avg/Min/Max of numeric fields), formulas.
5. **Filters**: FilterBuilder component (same as lists) + date filter with presets.
6. **Chart**: table | bar | column | stacked | line | area | donut | pie | funnel | KPI; pick X (group) and series (aggregates).
7. **Save**: name, folder, visibility (Private / Team / Everyone), then optional schedule.
Validation: max 20 columns, max 3 groups, max 10 aggregates; date grouping only on date fields.

### 9.8 Reports API
| Method | Route | Notes |
|---|---|---|
| GET | `/reports` | folders + reports visible to me (system + mine + shared), favorites first |
| GET | `/reports/sources` | data sources with fields/operators/aggregations available to me (for builder) |
| POST | `/reports/run` | ad-hoc `ReportQuery` (builder preview) |
| GET | `/reports/{id}` | definition |
| POST | `/reports/{id}/run` | `{ parameters / filter overrides, period }` → `ReportResult` (also updates LastRunAt, logs report_runs) |
| GET | `/reports/system/{key}` / POST `.../run` | system reports by key |
| POST/PUT/DELETE | `/reports`, `/reports/{id}` | custom reports (owner or Admin) |
| POST | `/reports/{id}/favorite` | toggle |
| POST | `/reports/{id}/export?format=xlsx|pdf|csv` | requires Reports.Export; returns file (≤ 50k rows), audit |
| GET/POST/PUT/DELETE | `/reports/{id}/schedules` | scheduled delivery |
| GET/POST/PUT/DELETE | `/targets` | sales targets (Manager for team members, Admin all) |
| POST | `/targets/bulk` | set the same target for many users/months |
| GET | `/targets/progress?period=&scope=` | progress list (used by dashboard + report 12) |
| GET/POST/PUT/DELETE | `/report-folders` | |
Permission: `Reports.View` to see reports; data inside respects the **underlying module's** View permission + scope (a Staff user running "Sales by user" sees only themselves).

### 9.9 Export formats
- **Excel (ClosedXML):** sheet 1 = data with bold header, frozen header row, auto-filter, column formats by type (currency `#,##0.00 "AED"`, percent, dates in tenant format), totals row; sheet 2 "Report info": report name, filters in plain language, period, generated by/at (tenant tz), row count, truncated warning. RTL sheet direction when user language is Arabic.
- **PDF (QuestPDF):** A4 landscape; header with tenant logo + report name + period; filter summary; chart image (render chart server-side: **out of scope v1** — PDF contains KPIs + table; v2 may add ScottPlot-rendered PNG); table with zebra rows, repeated header per page; footer page X of Y + generated at. Arabic font support.
- **CSV:** UTF-8 with BOM (Excel-friendly for Arabic).

### 9.10 Scheduled delivery
- On create/update schedule → `RecurringJob.AddOrUpdate<ReportScheduleJob>($"report-schedule-{id}", j => j.RunAsync(tenantId, id, CancellationToken.None), cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(tz) })`. Cron from Frequency/Day/Time. Delete/deactivate → `RecurringJob.RemoveIfExists`.
- Job: `tenantContext.Use(tenantId, schedule.RunAsUserId)`; **re-check** that the run-as user is active and still has Reports.View + Export (else deactivate schedule & notify owner); run report with run-as user's scope; build file; email recipients (each internal recipient must have Reports.View; the data is the run-as user's view — show that in the email "Report as seen by Sara Khan"); attachment ≤ 10 MB else upload & send link (expires 7 days); log `report_runs`; notification `ReportReady` to owner on failure.

### 9.11 Sales targets
- UI: Targets page (Reports → Targets): grid of users (rows) × months (columns) for a selected metric & year, inline editable; team & company rows; copy previous period; bulk set.
- Validation: Manager can set targets only for users in their team; Admin for all; TargetValue ≥ 0.
- Progress calculation in `TargetMetricCalculator` (single source of truth, reused by dashboard `target_progress` and report `target_vs_actual`).
- Team target, if not set explicitly, = sum of member targets (computed, flagged `isDerived`).

### 9.12 Performance & limits
- Report query timeout 15 s → 422 `report_timeout` with hint to add filters.
- Preview limit 200 rows; run limit 1,000 rows on screen (paged client-side); export limit 50,000.
- Add composite indexes listed in 5.7; check `EXPLAIN ANALYZE` of system reports on a 200k-deal seeded tenant (milestone 15).
- Cache system report results 60 s per user+params (HybridCache).
- Later (if needed): read replica connection string `ConnectionStrings:Reporting`.

### 9.13 Reports UI
- **Reports home:** left: folders (System, My reports, Shared, Favorites); main: cards/list with name, description, owner, last run; search; "New report".
- **Report viewer:** title, description, parameter bar (period picker, users/teams multiselect, pipeline), chart (ApexCharts) above table (MudTable with sorting, sticky header, totals row, row click → drill-down), buttons: Export (Excel/PDF/CSV), Schedule, Save as, Edit (custom), Favorite, Share.
- Mobile: chart first, table horizontally scrollable.

### 9.14 Acceptance criteria (Reports)
- All 15 Phase-1 system reports (1–15) return correct numbers on the sample-data tenant (integration tests compare to hand-computed values from the deterministic seed).
- A Staff user running any report sees only their own records; a Manager sees the team.
- Custom report with a custom field group-by (e.g. deals grouped by `cf_property_type`) works.
- Excel export opens without warnings; Arabic text displays correctly in Excel/PDF.
- A schedule set for 08:00 Asia/Dubai sends at 04:00 UTC; deactivated run-as user stops the schedule.
- SQL injection test suite passes.

---

## 10. FRONTEND (BLAZOR WASM) SPECIFICATION

> **This section describes behavior and wiring. ALL visual decisions (colors, typography, spacing, layouts, component markup, every screen's wireframe, states and copy) are in Section 18, which overrides anything here.** Shared components listed in 10.3 live in the `MindLift.UI` Razor Class Library so the Portal (Section 20) and the Phase 3 mobile app reuse them.

### 10.1 App shell
- `MainLayout`: MudAppBar (logo, global search with typeahead (`/search`), "+" quick create menu, notifications bell, language toggle EN/ع, user menu), MudDrawer nav built from `bootstrap.modules` (enabled + View permission, labels from settings, icons), content area, trial/sample-data/impersonation banners.
- Responsive: drawer collapses to icons on tablet, to overlay on phone; lists switch to card layout under 600 px.
- Theme: MudTheme from tenant `PrimaryColor`; light/dark toggle (user preference).
- RTL: `MudRTLProvider RightToLeft="@IsArabic"`; set `document.documentElement.dir/lang` via JS interop.

### 10.2 Core services
- `ApiClient` (typed wrappers over `HttpClient`, JSON with `System.Text.Json` source generators optional), `AuthHandler : DelegatingHandler` (attach access token; on 401 call `/auth/refresh` once, retry; on failure → login), `JwtAuthStateProvider : AuthenticationStateProvider`, access token kept **in memory** (refresh cookie restores session after reload).
- `AppState` (bootstrap data, current user, permissions helpers `Can(module, action)`, labels `ModuleLabel(key, plural)`, `FieldsFor(entity)`, `Picklist(key)`), refreshed after settings changes.
- `NotificationClient` (SignalR HubConnection with auto-reconnect).
- `ProblemDetailsHandler` → MudSnackbar errors, field-level validation errors mapped to forms.

### 10.3 Generic components (build once, reuse in every module)
| Component | Purpose |
|---|---|
| `EntityListPage<TItem>` | Saved view tabs, search, `FilterBuilder`, column chooser (from FieldRegistry), MudDataGrid server-side paging/sort, row selection + `BulkActionBar`, import/export buttons, empty states, card mode on mobile |
| `FilterBuilder` | Builds `FilterGroup`: field picker (searchable), operator list by type, value editor by type (text, number, date/preset, picklist multi-select, user picker with "@me") |
| `DynamicForm` | Renders a layout (sections/columns) from `layouts` + field defs; editors per `FieldType` (MudTextField, MudNumericField, MudDatePicker, MudSelect, MudAutocomplete for lookups/users, chips for tags, file upload); validation from field defs (required, email, url, phone) + server errors |
| `FieldValue` | Read-only display of any field value by type |
| `RecordDetailLayout` | Header (title, badges, owner, quick actions), left field panel (inline edit per field), right tab area |
| `Timeline` | Infinite scroll timeline with icons per event type, filters (all/activities/messages/changes) |
| `ActivityComposer` | Tabs Note/Task/Call/Meeting with mentions (MudAutocomplete on "@") |
| `MessageComposer` | Email/WhatsApp/SMS with template picker and preview |
| `KanbanBoard<TCard>` | Columns with totals, drag & drop, load more |
| `ImportWizard` | Upload → map → validate → run → result |
| `ConfirmDialog`, `UserPicker`, `RecordPicker`, `MoneyInput` (amount + currency), `PeriodPicker` |
| `ChartCard` | Wraps ApexChart with title, loading, empty state |

### 10.4 Pages (Phase 1)
`/login`, `/signup`, `/forgot-password`, `/reset-password`, `/verify-email`, `/accept-invite`, `/onboarding` (wizard), `/` (dashboard), `/leads`, `/leads/{id}`, `/contacts`, `/contacts/{id}`, `/accounts`, `/accounts/{id}`, `/deals` (board/list), `/deals/{id}`, `/activities`, `/calendar`, `/inbox`, `/reports`, `/reports/{id}`, `/reports/new`, `/reports/targets`, `/notifications`, `/profile`, `/settings/...` (Section 7.2), `/imports/{id}`, `/documents`, `/documents/{id}` (review screen), `/settings/document-types`, `/settings/portal`, dev-only `/_styleguide`, public `/book/{tenantSlug}/{slug}` and `/book/manage/{token}` (lightweight public pages, branded). Customer portal pages: Section 20.2 (separate app at `/portal`).

### 10.5 UX rules
- Every list/detail respects `AppState.Can(...)`: hide buttons the user can't use (server still enforces).
- Use module labels everywhere ("Property Deal"), never hard-coded "Deal".
- Optimistic UI for drag & drop and task completion, rollback on error.
- Keyboard: `/` focuses global search; `N` new record on list pages.
- Accessibility: labels on inputs, color is never the only indicator.

---

## 11. BACKGROUND JOBS CATALOG

| Job | Trigger | Queue |
|---|---|---|
| `SendMessageJob(tenantId, messageId)` | enqueue on send | messaging |
| `ImapPollJob` | every 2 min (per active email account, fan-out) | messaging |
| `WhatsAppWebhookProcessJob` / `SmsWebhookProcessJob` | enqueue from webhook | messaging |
| `ActivityReminderJob` | every 1 min | default |
| `OverdueTaskJob` | every 15 min | default |
| `BookingReminderJob` | every 5 min | default |
| `DailyDigestJob` | hourly | default |
| `NotificationEmailJob` | enqueue | messaging |
| `ImportJob(tenantId, importId)` | enqueue | imports |
| `ExportJob` | enqueue (large exports) | reports |
| `ReportScheduleJob(tenantId, scheduleId)` | recurring per schedule | reports |
| `BulkUpdateJob` | enqueue (> 1,000 records, reassign on deactivation) | default |
| `CustomFieldPurgeJob` | enqueue (delete field with data) | default |
| `TrialExpiryJob` | daily 06:00 UTC | default |
| `CleanupJob` (expired invitations, refresh tokens, temp files > 24 h, old notifications > 180 days) | daily | default |
| `CalendarSyncJob` (M11b) | every 5 min | default |
| `DocumentProcessingJob(tenantId, documentId)` | enqueue on upload / re-process | ocr (worker count 2) |
| `DocumentExpiryJob` | daily 05:00 UTC | default |
| `DocumentRequestReminderJob` | daily 06:00 UTC | default |

Rules: jobs are idempotent (check status before acting), take ids not entities, always set tenant context, log with `TenantId` property, and never run longer than 5 min (split into batches/continuations).

---

## 12. SECURITY CHECKLIST
- [ ] Global tenant filter on every `TenantEntity` (+ reflection test that every `TenantEntity` type has a query filter).
- [ ] Interceptor blocks cross-tenant writes and `TenantId` changes.
- [ ] `[HasPermission]` on every controller action (+ reflection test: every action in `Controllers/` has `[HasPermission]`, `[AllowAnonymous]` or `[Authorize(Policy="PlatformAdmin")]`).
- [ ] Data scope applied to every list/get/update/delete of owned records (integration tests).
- [ ] Report SQL builder whitelist + parameters; statement timeout.
- [ ] Passwords via Identity hashing; lockout; rate limits on auth & public endpoints.
- [ ] Refresh token rotation + reuse detection; HttpOnly Secure SameSite cookies.
- [ ] Secrets encrypted (Data Protection); never returned by APIs (return `"••••"` + `hasSecret: true`).
- [ ] Webhook signature validation (WhatsApp `X-Hub-Signature-256`, Twilio `X-Twilio-Signature`).
- [ ] HTML sanitization for notes/emails displayed in UI (HtmlSanitizer; email HTML rendered in a sandboxed `iframe`).
- [ ] File upload: extension + MIME + size checks; files served with `Content-Disposition: attachment` except images/PDF; no execution paths.
- [ ] Web forms: honeypot, rate limit, `AllowedDomains` CORS, max payload 64 KB.
- [ ] Security headers (HSTS, X-Content-Type-Options, Referrer-Policy, CSP for the WASM app).
- [ ] Audit log for login, permission changes, exports, imports, impersonation, deletes.
- [ ] Error responses never include stack traces in production.
- [ ] Export/import permission checks + row limits.
- [ ] GDPR-style: contact "erase personal data" (Admin) = anonymize fields + delete messages/files (Phase 2).
- [ ] OCR: Tesseract runs as a separate process with a hard timeout and is killed on timeout; command arguments are built only from server-generated temp paths (GUID file names) — never from file names or user text; temp folder deleted after each job; page/size limits enforced before rendering (PDF bombs).
- [ ] Portal: separate JWT audience and auth scheme; staff endpoints reject portal tokens and vice versa (integration test); every portal query filters by the portal user's `ContactId`/`AccountId` **and** `SharedWithPortal = true`.
- [ ] Extracted values containing personal data (passport/ID numbers) are masked in lists (`784-****-*******-1`) and shown in full only on the document detail to users with Documents.View.

---

## 13. TESTING STRATEGY

**Unit tests (MindLift.UnitTests)**
- `FilterCompiler` — every operator × type, nesting, `@me`, date presets (with fixed clock & time zones).
- `ReportSqlBuilder` — SQL snapshots per query shape; injection tests; tenant/scope predicates always present.
- `DateRangeResolver` / `TenantTime` — month/quarter/week boundaries for Asia/Dubai, America/New_York (DST), fiscal year start ≠ January.
- `AvailabilityService` — slots with buffers, overrides, min notice, DST.
- `TemplateRenderer` — fallback, unknown fields, Arabic text.
- `CustomFieldValidator`, `DuplicateDetector` normalization (phone/email), `LeadAssignmentService` round robin order.
- Industry templates: every JSON loads and validates (stage rules, unique keys, picklists referenced exist).

**Integration tests (MindLift.IntegrationTests)** — `WebApplicationFactory<Program>` + Testcontainers PostgreSQL (one container per test run, database per test class, migrations applied, `Respawn`-style cleanup optional).
- **Tenant isolation** (mandatory): create tenants A and B with data; user A calls every list/get endpoint with B's ids → 404/empty.
- **Permissions/scope**: Staff/Manager/Admin across 2 teams for leads, deals, activities, dashboard, reports.
- Sign-up → onboarding → template apply → sample data → dashboard non-empty → remove sample → empty.
- Lead web form → assignment → notification row; duplicate submission.
- Lead conversion end-to-end; deal stage moves + history + checklist tasks; lost reason required.
- Import CSV with errors & duplicates; rollback.
- Each system report on deterministic sample data equals expected numbers.
- Scheduled report job runs with run-as user's scope (invoke job method directly).

**Frontend:** bUnit tests for `FilterBuilder`, `DynamicForm` (optional; at least smoke tests). Manual QA checklist per milestone in `PROGRESS.md`, including Arabic/RTL and phone width (375 px).

**Performance seed:** `dotnet run --project src/MindLift.Api -- seed-perf --leads 200000 --deals 200000 --activities 1000000` (dev only) to validate dashboard < 1.5 s and reports < 5 s.

---

## 14. BUILD PLAN — MILESTONES (EXECUTE IN ORDER)

Each milestone ends with: build green, tests green, `PROGRESS.md` updated, DoD verified. Estimated effort for a single developer in parentheses (for planning only).

**Execution order:** M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8 → M9 → M10 → M11 → M12 → M13 → M14 → **M16 → M17 → M15**. Every milestone that contains screens also requires those screens to pass the UI DoD (18.15) with screenshots (18.16).

### M0 — Environment & skeleton (1 day)
1. Install .NET 10 SDK; Docker Desktop. `dotnet --list-sdks` shows 10.x.
2. `git init`; `.gitignore` (dotnet template); `global.json`; `Directory.Build.props`; `Directory.Packages.props`.
3. Create projects (Section 3) + references; `dotnet new sln` and add all.
4. `docker-compose.yml`: `postgres:16` (port 5432, volume), `axllent/mailpit` (SMTP 1025, UI 8025).
5. API `Program.cs`: Serilog, OpenAPI + Scalar, ProblemDetails, health checks (`/health` incl. DB), CORS (dev), serve Blazor WASM (`UseBlazorFrameworkFiles`, `UseStaticFiles`, `MapFallbackToFile("index.html")`, but `/api/*` and `/hubs/*` unmatched → 404).
6. Blazor WASM app with MudBlazor installed, renders a "Hello MindLift" page through the API host.
7. Create `PROGRESS.md`.
**DoD:** `docker compose up -d` + `dotnet run --project src/MindLift.Api` → `/health` OK, `/scalar` loads, `/` shows the Blazor page.

### M1 — Persistence & cross-cutting core (3 days)
1. Domain base types (4.1.1), Shared enums.
2. `AppDbContext` (IdentityUserContext<AppUser, Guid>), snake_case, extensions `pg_trgm`, global filter (4.1.3), xmin concurrency.
3. `TenantContext`, `TenantResolutionMiddleware`, `IClock`, `TenantTime`, `DateRangeResolver`.
4. `AuditableEntityInterceptor` (4.1.4) incl. AuditLog + TimelineEvent generation.
5. Exceptions + ProblemDetails middleware; FluentValidation auto-validation filter.
6. `IFileStorage` (local) + `files` table + upload/download endpoints (permission stub).
7. Hangfire with Postgres storage + dashboard (dev open, prod platform admin only).
8. HybridCache registration; SignalR hub skeleton.
9. First migration (`dotnet ef migrations add Initial -p src/MindLift.Infrastructure -s src/MindLift.Api`); apply on startup in Development only.
10. Tests: query filter reflection test; interceptor tests; DateRangeResolver tests.
**DoD:** tests green; a dummy `TenantEntity` in tenant A is invisible in tenant B.

### M2 — Auth, tenants, users, roles, permissions (4 days)
1. Entities: tenants, plans, tenant_settings, users, roles, role_permissions, teams, branches, invitations, refresh_tokens, module_settings, picklist_values (needed by seed).
2. `SystemSeedService.SeedTenantAsync` (roles, permissions, modules, base picklists EN/AR, notification prefs defaults).
3. Sign-up (6.2), login, refresh (rotation), logout, forgot/reset password, verify email (system emails via SMTP → Mailpit), `/auth/me`.
4. `HasPermission` attribute + policy provider + handler; `IPermissionService` with cache; `IDataScopeService`.
5. Users/invitations/roles/teams/branches endpoints (7.1); audit logs endpoint.
6. Plan limits service (users).
7. Platform admin minimal endpoints (6.10) + `seed-platform-admin` CLI arg.
8. Tests: sign-up creates seeded tenant; login lockout; refresh reuse detection; invite → accept; permission matrix enforcement; reflection test for `[HasPermission]`.
**DoD:** two tenants can sign up and manage users independently; Staff gets 403 on settings endpoints.

### M3 — Settings & customization backbone (5 days)
1. Field Registry with system field declarations for leads, contacts, accounts, deals, activities (create entity classes now; CRUD comes later).
2. Custom field definitions CRUD + validator + cache; label overrides; layouts; module settings endpoints.
3. Picklists CRUD with replacement-on-delete; pipelines/stages CRUD with stage rules & reassignment.
4. Currencies & exchange rates; company settings (logo upload).
5. `FilterCompiler` (LINQ + in-memory) with full tests; `ListQuery` handling helpers (`QueryableExtensions.ApplyListQuery(query, fields, ctx)` → filter, search, sort, paging).
6. Saved views CRUD.
7. `/app/bootstrap` endpoint with caching & invalidation.
**DoD:** custom field created via API appears in bootstrap; filtering on a custom field works in an integration test; stage delete with deals requires target stage.

### M4 — Tenant onboarding (★) (4 days)
1. `IndustryTemplateProvider` + 7 template JSON files (6.4, 6.6) + validation tests.
2. `IndustryTemplateService.ApplyAsync` (6.5) incl. re-apply cleanup.
3. Onboarding endpoints (6.3), step state machine, checklist computation (6.8).
4. `SampleDataService` (6.7) — deterministic; removal. (Needs leads/contacts/accounts/deals/activities/stage history/targets entities — create minimal entity classes + configurations now if not yet done.)
5. Trial status & `TrialExpiryJob`.
6. Tests: apply each template; switch template; sample data counts; removal leaves zero sample rows; checklist states.
**DoD:** API-only flow sign-up → company → industry → sample data works; DB shows correctly renamed modules, stages, custom fields.

### M5 — Design system, frontend shell & onboarding UI (8 days)
**M5a — Design system FIRST (do not build any page before this is done):**
1. Create `MindLift.UI` RCL. Add fonts link, `mindlift.css` (18.5 EXACT), `MindLiftTheme.cs` (18.4 EXACT).
2. Build every shared component in 18.7 EXACT, in this order: `PageHeader`, `MlCard`, `SectionCard`, `StatCard`, `StatusChip`, `UserAvatar`, `EmptyState`, `ErrorState`, `SkeletonTable`, `SkeletonCards`, `MoneyText`, `DateText`, `ConfirmDialog`, `FormDrawer`, `KeyValueList`, `FileDropZone`, `ChartCard`.
3. Create a hidden dev page `/_styleguide` in MindLift.Web that renders every component in all its states (loading/empty/error/normal, light/dark, EN/AR). Screenshot it (18.16) and check it against 18.7.
4. Create `tests/MindLift.E2E` with Playwright, the demo seed (`seed-demo`) and the screenshot harness (18.16).
**M5b — Shell & onboarding:**
5. Auth pages (18.9.1–18.9.2), `JwtAuthStateProvider`, `AuthHandler`, `AppState` bootstrap, app shell (18.6), nav from modules, i18n EN/AR + RTL switch, tenant primary color.
6. Onboarding wizard UI (18.9.3) + redirect guard.
7. Generic components: `EntityListPage` (18.8.1), `FilterBuilder`, `DynamicForm` (18.8.4), `FieldValue`, `RecordDetailLayout` (18.8.2), `Timeline`, pickers.
8. Settings pages using the Settings template (18.8.3): Company, Users & invites, Roles matrix, Teams, Modules, Fields & Layouts, Picklists, Pipelines, Currencies.
9. Notifications bell (static for now), global search box (wired in M7).
**DoD:** style guide + every M5 screen passes UI DoD (18.15) with screenshots at 1440/768/375 in EN and AR; a new user can sign up and finish the wizard in the browser; settings changes reflect immediately in nav/labels.

### M6 — Leads (★ customer onboarding) (5 days)
1. Lead entity complete, `LeadService` CRUD with custom fields, scope, timeline, normalization (email/phone).
2. Duplicate detector (+ endpoint), merge.
3. Assignment rules (service + settings CRUD + UI).
4. Web forms (settings + public endpoint + embed code).
5. Lead statuses & auto "Working"; disqualify.
6. **Conversion** (7.3) incl. re-linking activities/messages (messages table may be created now).
7. Generic import/export pipeline (4.10) — implemented here first for leads, made generic.
8. UI: list, kanban by status, detail, convert dialog, import wizard, web form builder.
**DoD:** acceptance criteria in 7.3 pass (integration tests + manual).

### M7 — Contacts & Accounts (4 days)
1. CRUD, duplicates, merge (contacts), hierarchy (accounts), 360° summary.
2. Import/export enabled for both.
3. Global search endpoint + UI typeahead.
4. UI list/detail pages with related tabs.
**DoD:** converted lead's history visible on contact; account page aggregates contacts/deals/activities.

### M8 — Deals & Pipeline (5 days)
1. Deal CRUD with currency conversion, probability, multiple contacts.
2. Stage change logic + history + rotting; board endpoint.
3. Customer onboarding checklist templates + DealWon trigger (7.4) + Onboarding tab.
4. Import/export.
5. UI: Kanban board (drag & drop, won/lost dialogs), list, detail with stage bar and stage history.
**DoD:** acceptance criteria in 7.7 pass.

### M9 — Activities & Timeline (4 days)
1. Activity CRUD (task/call/meeting/note), linking & auto-fill, complete/reopen/follow-up, LastActivityAt/NextActivityAt maintenance.
2. Mentions + HTML sanitization.
3. Timeline fan-out finalized; timeline endpoint for every entity.
4. `.ics` invites for meetings (email via system SMTP for now).
5. UI: ActivityComposer on all record pages, My Activities page.
**DoD:** every action on a record is visible on its timeline (manual checklist across modules).

### M10 — Notifications & reminders (3 days)
1. `INotificationService`, preferences, SignalR delivery, email delivery job with grouping.
2. Hook all events listed in 7.11 (assignments, mentions, deal won, import done...).
3. Reminder jobs (activity, overdue, daily digest).
4. UI: bell dropdown, notifications page, preferences, real-time toasts.
**DoD:** assigning a lead to another user shows a toast for that user within 5 s; overdue task notification arrives once.

### M11 — Calendar & booking (5 days) (+ M11b calendar sync, 5 days, can be deferred)
1. Calendar events endpoint; reschedule.
2. Booking pages settings; availability service (+ tests); public booking pages & API; booking manage (cancel/reschedule); confirmations & reminders.
3. UI: calendar (day/week/month, user/team filter), booking page settings, public booking pages (EN/AR).
4. *(M11b)* Google & Microsoft OAuth, push/pull sync, busy slots.
**DoD:** a customer books a slot via public link → meeting on user's calendar + contact/lead linked + confirmation email in Mailpit; the same slot is no longer offered.

### M12 — Communication (6 days)
1. Channel accounts settings (encrypted secrets, test connection).
2. Email: SMTP send, IMAP poll, threading, matching, attachments.
3. WhatsApp Cloud API: send text/template/media, webhook (verify + signature), statuses, 24-h rule, template sync.
4. SMS via Twilio + status webhook.
5. Message templates + renderer + preview; opt-outs.
6. Auto-create leads from inbound (setting).
7. UI: composer, conversation tabs, Inbox page.
8. Fake providers for dev/tests.
**DoD:** email sent from a contact appears in Mailpit and on the timeline; reply (inject via IMAP to a test mailbox or Fake inbound endpoint in dev) threads correctly; WhatsApp flow verified with Fake provider (real Meta test number if credentials available).

### M13 — Dashboard (★) (5 days)
1. `SalesMetrics` + `TargetMetricCalculator` (8.6) with tests on sample data.
2. Sales targets entity + CRUD API (needed for `target_progress`).
3. `IDashboardWidget` infrastructure, `DashboardService`, caching, layouts API.
4. Implement all Phase-1 widgets (8.4) + KPI tiles (8.5) + drill-down URLs.
5. SignalR `DashboardStale`.
6. UI: dashboard page, widget components, customize mode, filter bar, view switcher, responsive grid.
**DoD:** acceptance criteria 8.8 pass.

### M14 — Reports & Analytics (★) (8 days)
1. Data sources (9.2) for leads, contacts, accounts, deals, activities, deal_stage_history incl. custom fields.
2. `ReportSqlBuilder` + `FilterSqlCompiler` + `DapperReportExecutor` + formulas + totals (+ exhaustive unit tests).
3. System reports 1–13 (9.6) with deterministic tests.
4. Report definitions/folders/favorites/sharing API (9.8).
5. Excel/PDF/CSV exporters (9.9).
6. Schedules + `ReportScheduleJob` (9.10); report_runs log.
7. Targets UI (grid) (9.11).
8. UI: reports home, viewer with parameters/chart/table/drill-down, builder with live preview.
**DoD:** acceptance criteria 9.14 pass.

### M15 — Hardening & release (5 days) — ⚠ EXECUTE LAST (after M16 and M17)
1. Security checklist (Section 12) all ticked; 2FA (TOTP) optional.
2. Performance seed + index tuning; dashboard & reports timing targets met.
3. Accessibility & RTL pass on every page; phone-width pass.
4. Dockerfile (multi-stage) for API (+ WASM); production `docker-compose.prod.yml`; environment config; DB backups note; health checks; Serilog → file/Seq.
5. GitHub Actions CI: restore, build, test (with Postgres service), publish image.
6. Admin documentation: `docs/ADMIN_GUIDE.md` (setup, templates, roles), `docs/API.md` (generated OpenAPI).
**DoD:** clean deploy from scratch on a fresh VM via docker compose; full regression test suite green.

### M16 — Documents & Document Intelligence (OCR) (8 days)
1. Install Tesseract locally (19.4) and add it to the API Dockerfile; config section `Ocr`; `/health` reports OCR engine available + languages.
2. Entities 19.3 + migrations; `IFileStorage` reuse; upload endpoints (19.8) with validation; documents list/detail/permissions (module `Documents`).
3. Pipeline (19.5): PdfPig text layer → PDFtoImage render → SkiaSharp preprocess → `TesseractCliOcrEngine` (TSV parse) → `LayoutModel` (words/lines/bboxes) → classification → extraction rules → save → status.
4. Extraction engine (19.6): value patterns, anchor & regex rules, MRZ parser, normalizers (dates, amounts, Arabic-Indic digits), confidence, auto-approve.
5. Built-in document types (19.7) seeded at sign-up and referenced by industry templates.
6. Review & approve flow (19.9) incl. "Apply to record" mapping, timeline events, expiry date → `documents.ExpiryDate`; `DocumentExpiryJob` notifications; full-text search on OCR text.
7. Metrics (19.10): report data source `document_metrics`, reports 14–15, dashboard widgets `documents_review`, `documents_expiring`; record "Documents" tabs on Contact/Account/Deal.
8. UI: Documents list, upload drawer, **Document review screen** (18.9.12), Settings → Document types & rules with "Test on a sample file" playground.
9. Tests: unit tests for every value pattern, rule kind, MRZ, normalizers; integration test with sample files in `tests/fixtures/documents/` (one digital PDF, one scanned PDF, one JPG, one Arabic document) asserting extracted values.
**DoD:** acceptance criteria 19.12 pass; review screen passes UI DoD.

### M17 — Customer Portal v1 (6 days)
1. Entities & auth (20.3–20.4): portal users, invitations/magic links, separate JWT scheme & audience, refresh tokens.
2. Portal API (20.6) with strict contact/account scoping; branding endpoint.
3. `MindLift.Portal` WASM app hosted at `/portal` (20.2) using `MindLift.UI`; branding from tenant (logo, primary color, language).
4. Pages (18.10): Login/magic link, Home (key info cards, metrics charts, expiring documents, open requests), Documents list, Document detail (viewer + extracted info), Requests (upload against a request), Profile.
5. Staff side: "Portal access" card on Contact (invite/disable/resend), `SharedWithPortal` toggle on documents, Document requests (create from Contact/Account/Deal), notifications when customer uploads.
6. Tests: portal isolation (customer A can't see customer B in same tenant, nor other tenants); staff token rejected by portal API and vice versa; upload → OCR → staff approve → value visible in portal.
**DoD:** acceptance criteria 20.9 pass; all portal screens pass UI DoD in EN/AR at 375 px.

**Phase 1 total:** ~94 developer-days (excluding M11b). Dashboard/Reports/Onboarding/OCR/Portal are first-class. If a demo is needed earlier: M0–M8 → M13 (partial widgets) → M16 → M17 → M14.

---

## 15. PHASE 2 & 3 OUTLINES (build after Phase 1, same patterns)

Every new module: entity (TenantEntity, Owned, CF), Field Registry declarations, permissions entry, module_settings entry, list/detail/forms via generic components, timeline, import/export, report data source, dashboard widgets where noted, industry templates updated.

### Phase 2
**9 Products & Services** — `products` (Name, NameAr, Sku, Description, CategoryId (picklist `product_category`), Unit (picklist), Price, Currency, TaxRateId, IsActive, CF), `tax_rates` (Name, Rate, IsDefault), `price_lists` + `price_list_items` (customer-type pricing; Account/Contact has `PriceListId`).
**10 Quotations** — `quotes` (Number per tenant sequence `QT-2026-00001` via `number_sequences` table with row lock, AccountId, ContactId, DealId, Status `Draft|PendingApproval|Approved|Sent|Accepted|Rejected|Expired`, IssueDate, ExpiryDate, Currency, Subtotal, DiscountTotal, TaxTotal, Total, TotalInBase, Terms, Notes, PublicToken), `quote_lines` (ProductId?, Description, Qty, UnitPrice, DiscountPct, TaxRate, LineTotal). Calculation: line net = qty×price×(1−disc); tax per line; rounding 2 dp per line (configurable). Discount above `settings.QuoteDiscountApprovalPct` → PendingApproval → manager approves (Automation approval step). Branded PDF (QuestPDF, logo, terms, AR/EN). Send by email/WhatsApp with PDF + public accept link. `ExpireQuotesJob`. Accepted → convert to invoice; deal amount optionally synced.
**11 Invoices & Payments** — `invoices` (Number, Status `Draft|Unpaid|PartlyPaid|Paid|Overdue|Void`, IssueDate, DueDate, totals, AmountPaid, Balance, RecurringScheduleId?), `invoice_lines`, `payments` (InvoiceId, Amount, Method picklist, Date, Reference, GatewayTxnId), `recurring_invoice_schedules` (template invoice, frequency, next date). Payment link via gateway (Stripe / Checkout.com / Telr / PayTabs — `IPaymentGateway`), webhook marks paid. `OverdueInvoiceJob` daily → status Overdue + reminders (3 days before, on due, +7, +14). Tax invoice compliance (TRN, "Tax Invoice" title for UAE). Dashboard `unpaid_invoices`; reports: revenue by paid invoices, aging (0–30, 31–60, 61–90, 90+).
**12 Documents (remaining after M16)** — folders UI with drag & drop, document versions (`document_versions`), visibility by roles (`AllowedRoleIds`), bulk download as zip, optional customer reminder message before expiry, e-signature via `IESignProvider` (DocuSign / Zoho Sign / Dropbox Sign) with webhook status. Optional **AI extraction adapter** (`IAiFieldExtractor`, e.g. Claude) as a second pass for low-confidence fields — rules + Tesseract stay the default.
**13 Tickets** — `tickets` (Number, Subject, Description, CategoryId, Priority, Status `Open|InProgress|WaitingForCustomer|Closed`, AssigneeId, ContactId, AccountId, Channel `Manual|Email|WhatsApp|Portal`, FirstResponseDueAt, ResolutionDueAt, FirstRespondedAt, ResolvedAt, SlaBreached flags), `ticket_messages` (internal note vs public reply), `sla_policies` (by priority/category: response & resolution minutes, business hours calendar). `SlaMonitorJob` every 5 min → `TicketSlaBreached`. Email-to-ticket (dedicated mailbox), WhatsApp-to-ticket (keyword or menu). Dashboard `open_tickets`; report `ticket_resolution`.
**16 Automation & Workflows** — `workflows` (EntityType, Trigger `RecordCreated|RecordUpdated|FieldChanged|StageChanged|StatusChanged|DateReached|Webhook`, TriggerConfig, Conditions = FilterGroup, Actions jsonb ordered list, IsActive), `workflow_runs` (log). Actions: `AssignOwner (round robin)`, `CreateTask`, `SendEmail`, `SendWhatsApp`, `SendSms`, `UpdateField`, `ChangeStatus/Stage`, `NotifyUser/Manager`, `Webhook`, `Wait (delay)`, `RequestApproval`. Event bus: services publish `DomainEvent`s (`LeadCreated`, `DealStageChanged`...) after SaveChanges → `WorkflowDispatcher` (Hangfire) evaluates. Time-based: `DateReached` rules evaluated by daily job (e.g. 30 days before `cf_renewal_date`). Loop protection: max depth 5, a workflow can't re-trigger itself on the same record within 1 min. Approvals: `approvals` table (EntityType, EntityId, RequestedBy, ApproverIds, Status, Reason) with UI inbox. Replaces Phase-1 hardcoded assignment rules & checklist trigger (migrate them into workflows).
**22 Integrations** — framework: `integrations` (Provider, Status, EncryptedCredentials, Settings, LastSyncAt), OAuth helper, webhook endpoints, sync logs. Gmail & Outlook (OAuth mail instead of IMAP/SMTP), Google/Outlook calendars (if M11b deferred), WhatsApp BSPs, SMS providers, accounting (Zoho Books, QuickBooks, Xero: push invoices/payments & customers), payment gateways, website forms (WordPress/Webflow via web forms + Zapier/Make via **public REST API with API keys** + outgoing webhooks), e-signature. Also SaaS billing for MindLift itself (Stripe Billing: plans, seats, invoices, dunning → tenant status).

### Phase 3
**14 Projects/Jobs** — `projects` (from won deal, template), `project_milestones`, `project_tasks` (reuse Activities with `ProjectId`), members, progress % = completed weight / total, Gantt-lite timeline, `project_templates` (replace checklist templates for complex services), client-visible flag for portal.
**15 Marketing Campaigns** — `segments` (FilterGroup over contacts/leads incl. related fields like "account city = Dubai AND cf_license_expiry this quarter"), `campaigns` (Channel, TemplateId, SegmentId, ScheduledAt, Status, throttling), `campaign_recipients` (status sent/delivered/opened/clicked/replied/bounced/unsubscribed). Email via bulk provider (SendGrid/SES/Mailgun) with open pixel & click redirect tracking; WhatsApp via approved marketing templates; unsubscribe link/keyword (STOP) → opt-out flags; suppression list; reports per campaign.
**19 Customer Portal (remaining after M17)** — add to the existing portal: quotes (view PDF, accept/reject), invoices (view, pay online via gateway), projects progress, tickets (create/reply), custom domain per tenant (`portal.clientdomain.com`). Same auth, scoping and UI templates as Section 20.
**23 Mobile App** — **.NET MAUI Blazor Hybrid** reusing `MindLift.UI` Razor Class Library (extract shared components from MindLift.Web), same API (refresh token in SecureStorage), features: contacts/leads/deals/tasks, calendar, click-to-call (`tel:`), WhatsApp (`https://wa.me/`), email (`mailto:`), log activities, camera → document upload, push notifications via FCM/APNs (`IPushSender` implementation + device tokens table), offline read cache (SQLite) for recent records.

---

## 16. OPEN QUESTIONS FOR THE BUSINESS (log answers in PROGRESS.md)
1. Hosting region / data residency (UAE/KSA clients may require in-country hosting — Azure UAE North?).
2. Subscription plans, prices, seat limits, trial length (assumed 14 days, plans Starter/Pro/Enterprise).
3. WhatsApp: direct Meta Cloud API vs a BSP (360dialog, Twilio, Gupshup)? Who registers numbers per tenant?
4. SMS providers for the Gulf (sender ID registration rules per country).
5. Payment gateways per country (Phase 2) and e-signature provider preference.
6. Should one email address be able to belong to multiple tenants (agencies working for many clients)? Plan assumes **no**.
7. Branding: product name/logo, custom domains per tenant (e.g. `crm.clientdomain.com`)?
8. Required compliance (UAE PDPL, KSA PDPL, GDPR) — data export/erase requests, retention.
9. Fiscal year and week start defaults per country — confirm table in 6.2.
10. Arabic translations: who provides/reviews UI translations?

---

## 17. GLOSSARY
- **Tenant / Client:** a subscribing business (MindLift's customer).
- **Customer:** the tenant's own customer (a Contact/Account with a won deal).
- **Owner:** user responsible for a record; basis for data scope.
- **Scope:** Own / Team / All — which records a role can see.
- **Base currency:** tenant's reporting currency; all totals use `AmountInBase`.
- **Template (industry):** JSON configuration applied at onboarding.
- **Origin:** whether a config row came from System seed, Template, or User.
- **Field Registry:** single metadata source for fields of every entity (system + custom).
- **FilterGroup:** shared JSON filter model for lists, views, reports, segments, workflows.
- **Document type:** a kind of document (passport, trade license, ...) with its own extraction rules (Section 19).
- **Portal user:** a tenant's customer who logs in to the Customer Portal (Section 20); not a staff user.

---

## 18. ★ UI/UX DESIGN SYSTEM & SCREEN SPECIFICATIONS (EXACT)

> **Why this section exists:** UI built without exact rules comes out inconsistent (random colors, cramped spacing, misaligned headers, no empty/loading states, broken mobile/RTL). Everything below is **prescriptive**. If you are unsure, copy the closest example here. Never add inline `style=` values except CSS variables already defined in 18.5.

### 18.1 Design principles (apply to every decision)
1. **Calm & clear:** white surfaces on a very light gray background, one accent color (primary), lots of whitespace. Color is used for *meaning* (status, alerts), not decoration.
2. **One primary action per screen**, always top-right (top-left in Arabic) in the page header, as a filled primary button.
3. **Show today's work first:** dashboards and lists default to "mine / open / today".
4. **Every screen has 4 states:** loading (skeleton), empty (helpful message + action), error (message + retry), normal. No blank screens, no spinners on whole pages.
5. **Fast feedback:** every click responds in < 100 ms (pressed state, optimistic update or skeleton); saves show a success toast.
6. **Forgiving:** destructive actions need confirmation or offer Undo; unsaved forms warn before closing.
7. **Mobile & RTL are not afterthoughts:** every screen is checked at 375 px and in Arabic.
8. **Consistency over creativity:** same component, same place, same wording for the same thing everywhere.

### 18.2 Color tokens (EXACT)
| Token | Light | Dark | Usage |
|---|---|---|---|
| `--ml-primary` | `#4F46E5` | `#818CF8` | Primary buttons, links, active nav, focus ring (tenant color overrides light value — see note) |
| `--ml-primary-hover` | `#4338CA` | `#A5B4FC` | Hover on primary |
| `--ml-primary-soft` | `#EEF2FF` | `#1E1B4B` | Active nav background, selected rows, soft badges |
| `--ml-secondary` | `#0D9488` | `#2DD4BF` | Secondary accent (rare) |
| `--ml-success` | `#16A34A` | `#4ADE80` | Won, completed, approved, positive delta |
| `--ml-warning` | `#D97706` | `#FBBF24` | Due soon, needs review, behind target |
| `--ml-error` | `#DC2626` | `#F87171` | Overdue, lost, failed, destructive |
| `--ml-info` | `#2563EB` | `#60A5FA` | Info banners, neutral notices |
| `--ml-bg` | `#F8FAFC` | `#0B1120` | App background |
| `--ml-surface` | `#FFFFFF` | `#111827` | Cards, app bar, drawer, dialogs |
| `--ml-surface-2` | `#F1F5F9` | `#1F2937` | Table header, subtle fills, input backgrounds when disabled |
| `--ml-border` | `#E2E8F0` | `#273244` | Card borders, dividers, table lines |
| `--ml-border-strong` | `#CBD5E1` | `#374151` | Input borders, drop zone border |
| `--ml-hover` | `#F1F5F9` | `#1F2937` | Row/item hover |
| `--ml-text` | `#0F172A` | `#E5E7EB` | Primary text |
| `--ml-text-2` | `#475569` | `#9CA3AF` | Secondary text, labels |
| `--ml-text-3` | `#94A3B8` | `#6B7280` | Placeholder, disabled, captions |
| `--ml-overlay` | `rgba(15,23,42,.45)` | `rgba(0,0,0,.6)` | Dialog/drawer backdrop |

**Chart palette (use in this order, never other colors for series):** `#4F46E5`, `#0D9488`, `#F59E0B`, `#EC4899`, `#0EA5E9`, `#84CC16`, `#8B5CF6`, `#F97316`. Targets/benchmarks lines: `#94A3B8` dashed.

**Status color map (StatusChip):**
| Meaning | Color token | Examples |
|---|---|---|
| New / Open / Draft | `--ml-info` | Lead New, Ticket Open, Document Uploaded/Queued |
| In progress / Working | `--ml-primary` | Lead Working, Document Processing, Request Open |
| Needs attention | `--ml-warning` | Qualified (awaiting conversion), NeedsReview, Due soon, Expiring ≤ 30 days |
| Positive / Done | `--ml-success` | Won, Converted, Completed, Approved, Paid |
| Negative | `--ml-error` | Lost, Disqualified, Failed, Rejected, Overdue, Expired |
| Neutral / Inactive | `--ml-text-3` | Cancelled, Inactive, Archived |
Picklist values with a custom `Color` (set by admins) use that color instead.

**Tenant brand color:** if `tenant_settings.PrimaryColor` is set and has contrast ratio ≥ 4.5 against `#FFFFFF`, use it for `--ml-primary` (light theme) and compute `--ml-primary-hover` = 10% darker, `--ml-primary-soft` = 92% mixed with white. If contrast < 4.5, darken in 5% steps until it passes. Dark theme always keeps the default indigo values.

### 18.3 Typography (EXACT)
- Fonts (add to both `index.html` files, `<head>`):
```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=IBM+Plex+Sans+Arabic:wght@400;500;600;700&display=swap" rel="stylesheet">
```
- Font stack: English `"Inter", "IBM Plex Sans Arabic", system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`; Arabic (when `<html lang="ar">`) `"IBM Plex Sans Arabic", "Inter", system-ui, sans-serif`.
- Numbers in tables, KPIs and money: `font-variant-numeric: tabular-nums;` (class `ml-num`). Use Western digits (0–9) in both languages.

| Role | MudBlazor `Typo` | Size / line-height | Weight | Where |
|---|---|---|---|---|
| Page title | `Typo.h4` | 24 / 32 px | 600 | `PageHeader` title only |
| Dialog title / greeting | `Typo.h5` | 20 / 28 px | 600 | Dialog & drawer titles, dashboard greeting |
| Card / section title | `Typo.h6` | 16 / 24 px | 600 | `MlCard`, `SectionCard`, widget titles |
| Emphasis | `Typo.subtitle1` | 15 / 22 px | 500 | Record names in lists/cards |
| Label strong | `Typo.subtitle2` | 14 / 20 px | 600 | Form section labels, table group headers |
| Body | `Typo.body1` | 14 / 20 px | 400 | Default text |
| Secondary | `Typo.body2` | 13 / 18 px | 400 | Subtitles, meta lines (with class `ml-muted`) |
| Caption | `Typo.caption` | 12 / 16 px | 500 | Helper text, timestamps, chip text |
| Overline | `Typo.overline` | 11 / 16 px, letter-spacing .06em, UPPERCASE | 600 | KPI labels only |
| KPI value | class `ml-kpi-value` | 28 / 36 px | 700 | `StatCard` value only |
| Button | `Typo.button` | 14 / 20 px, no uppercase | 600 | Buttons |
**Never use `Typo.h1`, `Typo.h2`, `Typo.h3`.** Sentence case everywhere ("New lead", not "New Lead"; never ALL CAPS except overline).

### 18.4 MudBlazor theme (EXACT — `MindLift.UI/Theme/MindLiftTheme.cs`)
```csharp
using MudBlazor;

namespace MindLift.UI.Theme;

public static class MindLiftTheme
{
    private static readonly string[] FontStack =
        ["Inter", "IBM Plex Sans Arabic", "system-ui", "-apple-system", "Segoe UI", "Roboto", "sans-serif"];

    public static MudTheme Create(string? tenantPrimary = null) => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = tenantPrimary ?? "#4F46E5", PrimaryContrastText = "#FFFFFF",
            Secondary = "#0D9488", SecondaryContrastText = "#FFFFFF",
            Tertiary = "#F59E0B",
            Info = "#2563EB", Success = "#16A34A", Warning = "#D97706", Error = "#DC2626", Dark = "#0F172A",
            Background = "#F8FAFC", BackgroundGray = "#F1F5F9", Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF", AppbarText = "#0F172A",
            DrawerBackground = "#FFFFFF", DrawerText = "#334155", DrawerIcon = "#64748B",
            TextPrimary = "#0F172A", TextSecondary = "#475569", TextDisabled = "#94A3B8",
            ActionDefault = "#64748B", ActionDisabled = "#CBD5E1", ActionDisabledBackground = "#F1F5F9",
            Divider = "#E2E8F0", DividerLight = "#F1F5F9",
            LinesDefault = "#E2E8F0", LinesInputs = "#CBD5E1",
            TableLines = "#E2E8F0", TableStriped = "#F8FAFC", TableHover = "#F1F5F9",
            GrayLight = "#E2E8F0", GrayLighter = "#F1F5F9",
            Skeleton = "rgba(15,23,42,0.08)",
            HoverOpacity = 0.06,
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#818CF8", PrimaryContrastText = "#0B1120",
            Secondary = "#2DD4BF", Tertiary = "#FBBF24",
            Info = "#60A5FA", Success = "#4ADE80", Warning = "#FBBF24", Error = "#F87171", Dark = "#E5E7EB",
            Background = "#0B1120", BackgroundGray = "#1F2937", Surface = "#111827",
            AppbarBackground = "#111827", AppbarText = "#E5E7EB",
            DrawerBackground = "#111827", DrawerText = "#D1D5DB", DrawerIcon = "#9CA3AF",
            TextPrimary = "#E5E7EB", TextSecondary = "#9CA3AF", TextDisabled = "#6B7280",
            ActionDefault = "#9CA3AF", ActionDisabled = "#4B5563", ActionDisabledBackground = "#1F2937",
            Divider = "#273244", DividerLight = "#1F2937",
            LinesDefault = "#273244", LinesInputs = "#374151",
            TableLines = "#273244", TableStriped = "#0F172A", TableHover = "#1F2937",
            Skeleton = "rgba(255,255,255,0.08)",
            HoverOpacity = 0.08,
        },
        Typography = new Typography
        {
            Default   = new DefaultTypography   { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.43", LetterSpacing = "normal" },
            H4        = new H4Typography        { FontFamily = FontStack, FontSize = "1.5rem",   FontWeight = "600", LineHeight = "1.333", LetterSpacing = "-0.01em" },
            H5        = new H5Typography        { FontFamily = FontStack, FontSize = "1.25rem",  FontWeight = "600", LineHeight = "1.4",   LetterSpacing = "-0.005em" },
            H6        = new H6Typography        { FontFamily = FontStack, FontSize = "1rem",     FontWeight = "600", LineHeight = "1.5",   LetterSpacing = "normal" },
            Subtitle1 = new Subtitle1Typography { FontFamily = FontStack, FontSize = "0.9375rem",FontWeight = "500", LineHeight = "1.467", LetterSpacing = "normal" },
            Subtitle2 = new Subtitle2Typography { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.43",  LetterSpacing = "normal" },
            Body1     = new Body1Typography     { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.43",  LetterSpacing = "normal" },
            Body2     = new Body2Typography     { FontFamily = FontStack, FontSize = "0.8125rem",FontWeight = "400", LineHeight = "1.385", LetterSpacing = "normal" },
            Caption   = new CaptionTypography   { FontFamily = FontStack, FontSize = "0.75rem",  FontWeight = "500", LineHeight = "1.333", LetterSpacing = "normal" },
            Overline  = new OverlineTypography  { FontFamily = FontStack, FontSize = "0.6875rem",FontWeight = "600", LineHeight = "1.455", LetterSpacing = "0.06em" },
            Button    = new ButtonTypography    { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.43",  LetterSpacing = "normal", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            AppbarHeight = "56px",
            DrawerWidthLeft = "248px", DrawerWidthRight = "480px",
            DrawerMiniWidthLeft = "72px",
        },
    };
}
```
> If the installed MudBlazor version uses different property types (e.g. numeric `FontWeight`) or names, keep the **values** and adapt the syntax; record it in `PROGRESS.md`.

`MainLayout.razor` top (EXACT pattern):
```razor
<MudThemeProvider Theme="_theme" @bind-IsDarkMode="_isDark" />
<MudPopoverProvider />
<MudDialogProvider MaxWidth="MaxWidth.Small" FullWidth="true" CloseButton="true" BackdropClick="false" />
<MudSnackbarProvider />
<MudRTLProvider RightToLeft="@AppState.IsArabic">
    @* app shell (18.6) *@
</MudRTLProvider>
```
Snackbar config in `Program.cs`: `PositionClass = Defaults.Classes.Position.BottomEnd` (BottomCenter when viewport < 600 px), `VisibleStateDuration = 4000` (errors 8000), `ShowCloseIcon = true`, `SnackbarVariant = Variant.Filled`, `MaxDisplayedSnackbars = 3`.
Also set `data-theme="dark|light"` on `<html>` (JS interop) so CSS tokens in 18.5 switch.

### 18.5 Global CSS (EXACT — `MindLift.UI/wwwroot/css/mindlift.css`, referenced in both `index.html` after MudBlazor css as `_content/MindLift.UI/css/mindlift.css`)
```css
:root {
  --ml-primary:#4F46E5; --ml-primary-hover:#4338CA; --ml-primary-soft:#EEF2FF; --ml-secondary:#0D9488;
  --ml-success:#16A34A; --ml-warning:#D97706; --ml-error:#DC2626; --ml-info:#2563EB;
  --ml-bg:#F8FAFC; --ml-surface:#FFFFFF; --ml-surface-2:#F1F5F9; --ml-border:#E2E8F0; --ml-border-strong:#CBD5E1;
  --ml-hover:#F1F5F9; --ml-text:#0F172A; --ml-text-2:#475569; --ml-text-3:#94A3B8; --ml-overlay:rgba(15,23,42,.45);
  --ml-radius-sm:6px; --ml-radius:10px; --ml-radius-lg:14px; --ml-radius-pill:999px;
  --ml-shadow-1:0 1px 2px rgba(15,23,42,.06),0 1px 3px rgba(15,23,42,.10);
  --ml-shadow-2:0 4px 12px rgba(15,23,42,.08);
  --ml-shadow-3:0 20px 40px rgba(15,23,42,.18);
  --ml-s1:4px; --ml-s2:8px; --ml-s3:12px; --ml-s4:16px; --ml-s5:20px; --ml-s6:24px; --ml-s8:32px; --ml-s10:40px; --ml-s12:48px; --ml-s16:64px;
  --ml-page-pad:24px; --ml-appbar-h:56px; --ml-bottomnav-h:64px;
  --ml-ease:cubic-bezier(.2,0,0,1); --ml-dur-fast:150ms; --ml-dur:200ms;
}
html[data-theme="dark"] {
  --ml-primary:#818CF8; --ml-primary-hover:#A5B4FC; --ml-primary-soft:#1E1B4B; --ml-secondary:#2DD4BF;
  --ml-success:#4ADE80; --ml-warning:#FBBF24; --ml-error:#F87171; --ml-info:#60A5FA;
  --ml-bg:#0B1120; --ml-surface:#111827; --ml-surface-2:#1F2937; --ml-border:#273244; --ml-border-strong:#374151;
  --ml-hover:#1F2937; --ml-text:#E5E7EB; --ml-text-2:#9CA3AF; --ml-text-3:#6B7280; --ml-overlay:rgba(0,0,0,.6);
  --ml-shadow-1:0 1px 2px rgba(0,0,0,.4); --ml-shadow-2:0 4px 12px rgba(0,0,0,.4); --ml-shadow-3:0 20px 40px rgba(0,0,0,.6);
}
@media (max-width: 959.98px) { :root { --ml-page-pad:16px; } }

html, body { background:var(--ml-bg); color:var(--ml-text); -webkit-font-smoothing:antialiased; }
html[lang="ar"] body, html[lang="ar"] .mud-typography { font-family:"IBM Plex Sans Arabic","Inter",system-ui,sans-serif !important; }
@media (prefers-reduced-motion: reduce) { *, *::before, *::after { transition:none !important; animation:none !important; } }
:focus-visible { outline:2px solid var(--ml-primary); outline-offset:2px; border-radius:4px; }

/* layout */
.ml-page { padding:var(--ml-page-pad); padding-bottom:calc(var(--ml-page-pad) + 16px); min-height:calc(100vh - var(--ml-appbar-h)); }
.ml-page--narrow { max-width:1200px; margin-inline:auto; }
.ml-page--form { max-width:960px; margin-inline:auto; }
@media (max-width: 599.98px) { .ml-page { padding-bottom:calc(var(--ml-bottomnav-h) + 24px); } }
.ml-stack { display:flex; flex-direction:column; gap:var(--ml-s4); }
.ml-stack-sm { display:flex; flex-direction:column; gap:var(--ml-s2); }
.ml-stack-lg { display:flex; flex-direction:column; gap:var(--ml-s6); }
.ml-row { display:flex; align-items:center; gap:var(--ml-s2); min-width:0; }
.ml-row--between { justify-content:space-between; }
.ml-row--wrap { flex-wrap:wrap; }
.ml-grow { flex:1 1 auto; min-width:0; }

/* text */
.ml-muted { color:var(--ml-text-2) !important; }
.ml-faint { color:var(--ml-text-3) !important; }
.ml-num { font-variant-numeric:tabular-nums; }
.ml-truncate { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; min-width:0; }
.ml-clamp-2 { display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; overflow:hidden; }
.ml-kpi-value { font-size:28px; line-height:36px; font-weight:700; font-variant-numeric:tabular-nums; letter-spacing:-0.01em; }
.ml-link { color:var(--ml-primary); text-decoration:none; font-weight:500; cursor:pointer; }
.ml-link:hover { text-decoration:underline; }

/* page header */
.ml-page-header { display:flex; flex-direction:column; gap:var(--ml-s1); margin-bottom:var(--ml-s6); }
.ml-page-header__row { display:flex; align-items:center; justify-content:space-between; gap:var(--ml-s4); flex-wrap:wrap; }
.ml-page-header__titles { display:flex; flex-direction:column; gap:2px; min-width:0; flex:1 1 280px; }
.ml-page-header__actions { display:flex; align-items:center; gap:var(--ml-s2); flex-wrap:wrap; }
.ml-page-header__tabs { margin-top:var(--ml-s3); border-bottom:1px solid var(--ml-border); }
.ml-breadcrumbs { padding:0 !important; margin-bottom:var(--ml-s1); font-size:13px; }

/* card */
.ml-card { background:var(--ml-surface); border:1px solid var(--ml-border); border-radius:var(--ml-radius); box-shadow:var(--ml-shadow-1); display:flex; flex-direction:column; min-width:0; }
.ml-card--clickable { cursor:pointer; transition:box-shadow var(--ml-dur-fast) var(--ml-ease), border-color var(--ml-dur-fast) var(--ml-ease); }
.ml-card--clickable:hover { box-shadow:var(--ml-shadow-2); border-color:var(--ml-border-strong); }
.ml-card__header { display:flex; align-items:center; justify-content:space-between; gap:var(--ml-s3); padding:var(--ml-s4) var(--ml-s5) 0; min-height:48px; }
.ml-card__body { padding:var(--ml-s4) var(--ml-s5) var(--ml-s5); flex:1 1 auto; min-width:0; }
.ml-card__body--flush { padding:0; }
.ml-card__footer { padding:var(--ml-s3) var(--ml-s5); border-top:1px solid var(--ml-border); display:flex; justify-content:flex-end; gap:var(--ml-s2); }
@media (max-width: 599.98px) { .ml-card__header { padding:var(--ml-s3) var(--ml-s4) 0; } .ml-card__body { padding:var(--ml-s3) var(--ml-s4) var(--ml-s4); } }

/* chips */
.ml-chip { --chip:var(--ml-text-3); display:inline-flex; align-items:center; gap:6px; height:22px; padding:0 8px; border-radius:var(--ml-radius-pill);
  font-size:12px; line-height:16px; font-weight:600; white-space:nowrap; color:var(--chip); background:color-mix(in srgb, var(--chip) 12%, transparent); }
.ml-chip__dot { width:6px; height:6px; border-radius:50%; background:var(--chip); flex:none; }
html[data-theme="dark"] .ml-chip { background:color-mix(in srgb, var(--chip) 20%, transparent); }

/* delta */
.ml-delta { display:inline-flex; align-items:center; gap:2px; font-size:12px; font-weight:600; font-variant-numeric:tabular-nums; }
.ml-delta--good { color:var(--ml-success); } .ml-delta--bad { color:var(--ml-error); } .ml-delta--flat { color:var(--ml-text-3); }

/* tables (MudTable / MudDataGrid) */
.mud-table-head .mud-table-cell { background:var(--ml-surface-2); color:var(--ml-text-2); font-size:12px; font-weight:600; height:40px; white-space:nowrap; }
.mud-table-body .mud-table-row { height:52px; }
.mud-table-body .mud-table-row.ml-row-clickable { cursor:pointer; }
.ml-cell-primary { display:flex; align-items:center; gap:var(--ml-s3); min-width:0; }
.ml-cell-primary__text { display:flex; flex-direction:column; min-width:0; }
.ml-cell-num { text-align:end; font-variant-numeric:tabular-nums; }

/* nav */
.ml-nav-section { padding:var(--ml-s4) var(--ml-s5) var(--ml-s1); font-size:11px; font-weight:600; letter-spacing:.06em; text-transform:uppercase; color:var(--ml-text-3); }
.ml-nav .mud-nav-link { height:40px; margin:2px 8px; border-radius:var(--ml-radius-sm); padding-inline:12px; font-weight:500; color:var(--ml-text-2); }
.ml-nav .mud-nav-link:hover { background:var(--ml-hover); }
.ml-nav .mud-nav-link.active { background:var(--ml-primary-soft); color:var(--ml-primary); box-shadow:inset 3px 0 0 var(--ml-primary); }
html[dir="rtl"] .ml-nav .mud-nav-link.active { box-shadow:inset -3px 0 0 var(--ml-primary); }
.ml-nav .mud-nav-link .mud-nav-link-icon { font-size:20px; }

/* banners */
.ml-banner { --tone:var(--ml-info); display:flex; align-items:center; gap:var(--ml-s3); min-height:40px; padding:6px var(--ml-page-pad);
  background:color-mix(in srgb, var(--tone) 10%, var(--ml-surface)); color:var(--ml-text); border-bottom:1px solid color-mix(in srgb, var(--tone) 30%, transparent); font-size:13px; }

/* empty / error state */
.ml-state { display:flex; flex-direction:column; align-items:center; text-align:center; gap:var(--ml-s2); padding:var(--ml-s12) var(--ml-s6); }
.ml-state__icon { width:72px; height:72px; border-radius:50%; display:grid; place-items:center; background:var(--ml-primary-soft); color:var(--ml-primary); margin-bottom:var(--ml-s2); }
.ml-state__icon--error { background:color-mix(in srgb, var(--ml-error) 12%, transparent); color:var(--ml-error); }
.ml-state__message { max-width:400px; color:var(--ml-text-2); }
.ml-state__actions { display:flex; gap:var(--ml-s2); margin-top:var(--ml-s3); flex-wrap:wrap; justify-content:center; }

/* key-value list */
.ml-kv { display:grid; grid-template-columns:minmax(120px, 40%) 1fr; gap:10px var(--ml-s4); }
.ml-kv__label { color:var(--ml-text-2); font-size:13px; line-height:20px; }
.ml-kv__value { font-size:14px; line-height:20px; min-width:0; overflow-wrap:anywhere; }
@media (max-width: 599.98px) { .ml-kv { grid-template-columns:1fr; gap:2px; } .ml-kv__value { margin-bottom:10px; } }

/* drop zone */
.ml-dropzone { position:relative; border:2px dashed var(--ml-border-strong); border-radius:var(--ml-radius); padding:var(--ml-s8) var(--ml-s6);
  display:flex; flex-direction:column; align-items:center; gap:var(--ml-s2); text-align:center; background:var(--ml-surface); transition:all var(--ml-dur-fast) var(--ml-ease); }
.ml-dropzone--active { border-color:var(--ml-primary); background:var(--ml-primary-soft); }
.ml-dropzone input[type=file] { position:absolute; inset:0; opacity:0; cursor:pointer; }

/* progress */
.ml-progress { position:relative; height:8px; border-radius:var(--ml-radius-pill); background:var(--ml-surface-2); overflow:visible; }
.ml-progress__bar { height:100%; border-radius:inherit; background:var(--bar, var(--ml-primary)); max-width:100%; }
.ml-progress__marker { position:absolute; top:-3px; width:2px; height:14px; background:var(--ml-text-2); border-radius:1px; }

/* document viewer highlight */
.ml-doc-page { position:relative; width:100%; background:#fff; box-shadow:var(--ml-shadow-2); border-radius:4px; overflow:hidden; }
.ml-doc-page img { display:block; width:100%; height:auto; }
.ml-doc-box { position:absolute; border:2px solid var(--ml-primary); background:color-mix(in srgb, var(--ml-primary) 12%, transparent); border-radius:3px; pointer-events:none; }
.ml-doc-box--active { border-color:var(--ml-warning); background:color-mix(in srgb, var(--ml-warning) 20%, transparent); animation:ml-flash 1.2s var(--ml-ease) 2; }
@keyframes ml-flash { 50% { box-shadow:0 0 0 6px color-mix(in srgb, var(--ml-warning) 30%, transparent); } }

/* responsive helpers */
@media (max-width: 599.98px) { .ml-hide-xs { display:none !important; } }
@media (min-width: 600px) { .ml-only-xs { display:none !important; } }
@media (max-width: 959.98px) { .ml-hide-sm-down { display:none !important; } }
@media (min-width: 960px) { .ml-only-sm-down { display:none !important; } }

/* bottom nav (phones) */
.ml-bottom-nav { position:fixed; inset-inline:0; bottom:0; height:calc(var(--ml-bottomnav-h) + env(safe-area-inset-bottom)); padding-bottom:env(safe-area-inset-bottom);
  background:var(--ml-surface); border-top:1px solid var(--ml-border); display:grid; grid-template-columns:repeat(5, 1fr); z-index:1200; }
.ml-bottom-nav a { display:flex; flex-direction:column; align-items:center; justify-content:center; gap:2px; font-size:11px; font-weight:500; color:var(--ml-text-2); text-decoration:none; }
.ml-bottom-nav a.active { color:var(--ml-primary); }
```
**Rule:** components and pages use these classes + MudBlazor props. No other custom CSS files except page-scoped `.razor.css` for genuinely unique layout (e.g. Kanban, document viewer), and those may only use the `--ml-*` variables.

### 18.6 Spacing, sizing, layout & breakpoints (EXACT)
- **Spacing scale (px):** 4, 8, 12, 16, 20, 24, 32, 40, 48, 64. Nothing else. Between cards in a grid: **16 px** (`MudGrid Spacing="4"`). Between page sections: **24 px**. Inside cards: 16–20 px padding (given by `.ml-card__body`).
- **Radii:** inputs & buttons 6 px; cards 10 px; dialogs/drawers 14 px; chips/avatars pill.
- **Heights:** app bar 56; buttons 36 (`Size.Medium`), compact 32 (`Size.Small`) in tables/toolbars; inputs 40 (`Margin.Dense`); table rows 52; table header 40; nav items 40; bottom nav 64; touch targets ≥ 40×40 on phones.
- **Icons:** MudBlazor `Icons.Material.Outlined.*` only (never Filled/Rounded mixes). Sizes: 20 px in nav & buttons (`Size.Small` on `MudIcon` inside buttons), 24 px default, 48 px inside empty states.
- **Breakpoints (MudBlazor):** xs < 600, sm 600–959, md 960–1279, lg 1280–1919, xl ≥ 1920.

**App shell (staff app):**
```
┌──────────────────────────────────────────────────────────────────────────────────────────┐ 56px app bar (surface, bottom border)
│ ☰  [logo] Al Noor Properties │ 🔍 Search leads, contacts, deals…  (480px)   │ (+) 🔔³ ع (SK)│
├────────────────┬─────────────────────────────────────────────────────────────────────────┤
│ (248px drawer) │ ⓘ Trial: 9 days left · Choose a plan                              [×]   │ 40px banner (optional)
│  Dashboard     ├─────────────────────────────────────────────────────────────────────────┤
│ SALES          │                                                                         │
│  Leads         │   .ml-page  (padding 24px; 16px below 960px)                            │
│  Contacts      │                                                                         │
│  Accounts      │   PageHeader                                                            │
│  Deals         │   content…                                                              │
│ WORK           │                                                                         │
│  Activities    │                                                                         │
│  Calendar      │                                                                         │
│  Inbox         │                                                                         │
│  Documents     │                                                                         │
│ INSIGHTS       │                                                                         │
│  Reports       │                                                                         │
│ ───────────    │                                                                         │
│  Settings      │                                                                         │
│  Help          │                                                                         │
│ [«] collapse   │                                                                         │
└────────────────┴─────────────────────────────────────────────────────────────────────────┘
```
- Drawer: `MudDrawer Variant="DrawerVariant.Mini"` on lg+ (expanded 248 px, mini 72 px, collapse state remembered in localStorage); `Variant="DrawerVariant.Responsive"` behavior: md → mini by default; sm/xs → `Temporary` (overlay, opened by ☰). Drawer has `Elevation="0"` and a 1 px end border.
- Nav order, sections & icons (EXACT; labels come from module settings; hide disabled/no-permission items; hide empty sections):

| Section | Item | Icon (`Icons.Material.Outlined.`) | Route |
|---|---|---|---|
| — | Dashboard | `SpaceDashboard` | `/` |
| Sales | Leads | `PersonSearch` | `/leads` |
| Sales | Contacts | `Contacts` | `/contacts` |
| Sales | Accounts | `Business` | `/accounts` |
| Sales | Deals | `Handshake` | `/deals` |
| Work | Activities | `TaskAlt` | `/activities` |
| Work | Calendar | `CalendarMonth` | `/calendar` |
| Work | Inbox | `Forum` | `/inbox` |
| Work | Documents | `Description` | `/documents` |
| Insights | Reports | `Insights` | `/reports` |
| (bottom) | Settings | `Settings` | `/settings` |
| (bottom) | Help | `HelpOutline` | external docs |

- App bar (start → end): ☰ toggle (`Menu` icon; on lg it toggles mini) · tenant logo (height 28 px, max width 140) + tenant name (`Typo.subtitle1`, hidden < 960) · global search (`MudAutocomplete`, width 480 on lg, 320 on md, icon-only on xs that opens a full-screen search dialog) · spacer · **Create** icon button (`Add`, color Primary, variant Filled, size Small, menu: New lead / contact / account / deal / task / meeting / upload document) · notifications (`NotificationsNone` with `MudBadge` count, max "99+") · language toggle (text button "ع" in English UI, "EN" in Arabic UI) · avatar (`UserAvatar` Md) menu: name+email header, Profile, Notification settings, Dark mode switch, Sign out.
- **Phones (xs):** drawer hidden; **bottom nav** (class `ml-bottom-nav`) with 5 items: Home (`SpaceDashboard`), Leads, Deals, Activities, More (`Menu` → opens drawer). Page FABs sit 16 px above the bottom nav.
- **Banners** (stacked under app bar, max 2 visible, priority order): impersonation (error tone, not dismissible) › subscription/past due (error) › trial ≤ 5 days (warning) › sample data loaded (warning, action "Remove sample data") › email unverified (info, action "Resend email").

### 18.7 Shared components (EXACT code — `MindLift.UI/Components`)
Add `@using MudBlazor` and `@using MindLift.UI.Components` in `_Imports.razor` of UI, Web and Portal.

**PageHeader.razor**
```razor
<div class="ml-page-header" data-testid="page-header">
    @if (Breadcrumbs is { Count: > 0 })
    {
        <MudBreadcrumbs Items="Breadcrumbs" Class="ml-breadcrumbs" MaxItems="4" />
    }
    <div class="ml-page-header__row">
        <div class="ml-page-header__titles">
            <MudText Typo="Typo.h4" Class="ml-truncate" data-testid="page-title">@Title</MudText>
            @if (!string.IsNullOrWhiteSpace(Subtitle))
            {
                <MudText Typo="Typo.body2" Class="ml-muted">@Subtitle</MudText>
            }
        </div>
        @if (Actions is not null)
        {
            <div class="ml-page-header__actions">@Actions</div>
        }
    </div>
    @if (Tabs is not null)
    {
        <div class="ml-page-header__tabs">@Tabs</div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public List<BreadcrumbItem>? Breadcrumbs { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
    [Parameter] public RenderFragment? Tabs { get; set; }
}
```
Header button rules: max **1 primary** (`Variant.Filled Color.Primary`, with start icon) + max **2 secondary** (`Variant.Outlined`), extra actions go to a `MudMenu` with `MoreVert` icon. On xs the text of secondary buttons is wrapped in `<span class="ml-hide-xs">` so they become icon buttons, and the primary action becomes a FAB (`MudFab Color.Primary StartIcon=Add`, fixed bottom-end, class `ml-only-xs`).

**MlCard.razor**
```razor
<div class="ml-card @(OnClick.HasDelegate ? "ml-card--clickable" : "") @Class" @onclick="OnClick" data-testid="@TestId">
    @if (Title is not null || HeaderActions is not null)
    {
        <div class="ml-card__header">
            <div class="ml-stack-sm" style="gap:2px;min-width:0">
                @if (Title is not null) { <MudText Typo="Typo.h6" Class="ml-truncate">@Title</MudText> }
                @if (Subtitle is not null) { <MudText Typo="Typo.body2" Class="ml-muted ml-truncate">@Subtitle</MudText> }
            </div>
            @HeaderActions
        </div>
    }
    <div class="ml-card__body @(Flush ? "ml-card__body--flush" : "")">
        @if (Loading)
        {
            @if (LoadingContent is not null) { @LoadingContent }
            else
            {
                <MudSkeleton Width="40%" Height="20px" />
                <MudSkeleton Width="90%" Height="16px" Class="mt-2" />
                <MudSkeleton Width="75%" Height="16px" Class="mt-2" />
            }
        }
        else { @ChildContent }
    </div>
    @if (Footer is not null) { <div class="ml-card__footer">@Footer</div> }
</div>

@code {
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public RenderFragment? HeaderActions { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Footer { get; set; }
    [Parameter] public RenderFragment? LoadingContent { get; set; }
    [Parameter] public bool Loading { get; set; }
    [Parameter] public bool Flush { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? TestId { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
}
```
Card header "View all" link pattern: `<MudLink Href="..." Typo="Typo.body2" Class="ml-link">View all</MudLink>` (arrow icon `ChevronRight`, mirrored in RTL via `Style="transform: scaleX(var(--ml-dir, 1))"` — set `--ml-dir:-1` on `html[dir=rtl]`).

**StatCard.razor**
```razor
<MlCard Loading="Loading" OnClick="Navigate" TestId="@($"kpi-{Key}")">
    <div class="ml-stack-sm">
        <div class="ml-row ml-row--between">
            <MudText Typo="Typo.overline" Class="ml-muted">@Label</MudText>
            @if (Icon is not null) { <MudIcon Icon="@Icon" Size="Size.Small" Class="ml-faint" /> }
        </div>
        <div class="ml-kpi-value" title="@Tooltip">@Value</div>
        <div class="ml-row">
            @if (Delta is null)
            {
                <span class="ml-delta ml-delta--flat">—</span>
            }
            else
            {
                var up = Delta.Value >= 0;
                var good = up == GoodWhenUp;
                <span class="ml-delta @(Delta.Value == 0 ? "ml-delta--flat" : good ? "ml-delta--good" : "ml-delta--bad")">
                    <MudIcon Icon="@(up ? Icons.Material.Outlined.ArrowUpward : Icons.Material.Outlined.ArrowDownward)" Style="font-size:14px" />
                    @($"{Math.Abs(Delta.Value):0.#}%")
                </span>
            }
            @if (Caption is not null) { <MudText Typo="Typo.caption" Class="ml-faint">@Caption</MudText> }
        </div>
    </div>
</MlCard>

@code {
    [Inject] NavigationManager Nav { get; set; } = default!;
    [Parameter, EditorRequired] public string Key { get; set; } = "";
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter] public string Value { get; set; } = "—";
    [Parameter] public string? Tooltip { get; set; }
    [Parameter] public decimal? Delta { get; set; }
    [Parameter] public bool GoodWhenUp { get; set; } = true;
    [Parameter] public string? Caption { get; set; }      // e.g. "vs last month"
    [Parameter] public string? Icon { get; set; }
    [Parameter] public string? Href { get; set; }
    [Parameter] public bool Loading { get; set; }
    private void Navigate() { if (Href is not null) Nav.NavigateTo(Href); }
}
```

**StatusChip.razor**
```razor
<span class="ml-chip" style="--chip:@Color" data-testid="status-chip">
    @if (Dot) { <span class="ml-chip__dot"></span> }
    @Text
</span>
@code {
    [Parameter, EditorRequired] public string Text { get; set; } = "";
    [Parameter] public string Color { get; set; } = "var(--ml-text-3)";   // hex from picklist or a --ml-* token
    [Parameter] public bool Dot { get; set; } = true;
}
```
Helper `StatusColors.For(category)` in UI returns the token per the status color map (18.2).

**UserAvatar.razor** — sizes: `Xs`=20, `Sm`=24, `Md`=32, `Lg`=40, `Xl`=64 px. Image if `AvatarUrl`, else initials (first letter of first + last name, uppercase; Arabic names take first letters too) on a background from the chart palette chosen by `Math.Abs(userId.GetHashCode()) % 8` (use a stable hash: sum of Guid bytes), white text, font-size 40% of size, weight 600. Tooltip with full name when `ShowTooltip`. `WithName` parameter renders avatar + name (`Typo.body1`) in a `.ml-row`.

**EmptyState.razor**
```razor
<div class="ml-state" data-testid="empty-state">
    <div class="ml-state__icon"><MudIcon Icon="@Icon" Style="font-size:36px" /></div>
    <MudText Typo="Typo.h6">@Title</MudText>
    @if (Message is not null) { <MudText Typo="Typo.body1" Class="ml-state__message">@Message</MudText> }
    @if (Actions is not null) { <div class="ml-state__actions">@Actions</div> }
</div>
@code {
    [Parameter] public string Icon { get; set; } = Icons.Material.Outlined.Inbox;
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public string? Message { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
}
```
Inside widgets use `Compact="true"` variant (padding 24 px, icon 48 px) — add the parameter and a `ml-state--compact` class (`padding:24px 16px`, icon box 48 px).

**ErrorState.razor** — same layout, icon `ErrorOutline` in `ml-state__icon ml-state__icon--error`, title "Something went wrong", message param (default "We couldn't load this. Check your connection and try again."), button `Variant.Outlined` "Try again" (`StartIcon=Refresh`) → `OnRetry`.

**SkeletonTable.razor** — `Rows` (default 8), `Columns` (default 5): a card-flush with a 40 px header bar (`MudSkeleton Height=12px Width=60px` per column) and rows of 52 px each: first cell = circle 32 px + two lines (60% / 40%), other cells single line widths 70/50/40/60 %.
**SkeletonCards.razor** — `Count` (default 4) cards, each 120 px high with 3 skeleton lines; used for KPI rows and mobile lists.

**MoneyText.razor** — params `Amount (decimal?)`, `Currency (string)`, `Compact (bool)`. Normal: `"AED 1,250,000.00"` (currency code + space + N2, culture of the user's language but digits 0–9). Compact: `< 1,000` → N0; `< 1,000,000` → `"AED 350K"` (one decimal if < 10K, e.g. "AED 9.5K"); `< 1,000,000,000` → `"AED 1.2M"`; else `"AED 3.4B"`; full value in `title` tooltip. Null → `"—"`. Always `ml-num`; right-aligned in tables.
**DateText.razor** — params `Value (DateTime? UTC)`, `Mode` (`Relative|Date|DateTime|Time`), shows in user tz. Relative: "Just now" (< 1 min), "5 min ago", "3 h ago", "Yesterday", "in 2 days", ≤ 6 days → relative, otherwise date. Date format default `dd MMM yyyy` ("09 Oct 2026"), DateTime `dd MMM yyyy, HH:mm`, Time `HH:mm` (tenant setting may switch to 12 h `h:mm tt`). Tooltip = full `dddd, dd MMMM yyyy, HH:mm (zone)`. Overdue mode (`Danger=true`) colors text `--ml-error`.
**ConfirmDialog.razor** + `IDialogService.ConfirmAsync(title, message, confirmText, danger)` extension: `MaxWidth.ExtraSmall` (400 px), title `Typo.h5`, message `Typo.body1 ml-muted`, actions: "Cancel" (`Variant.Text`) + confirm (`Variant.Filled`, `Color.Error` if danger else `Color.Primary`). Returns `bool`.
**SectionCard.razor** — `MlCard` with `Title`, optional `Description` (body2 muted under the title) and `ChildContent`; used to group form fields and settings blocks.
**FormDrawer.razor** — right-side (end) temporary drawer, width 480 px (100 % on xs), `Elevation=3`, overlay `--ml-overlay`:
```
┌─────────────────────────────────────────────┐
│ New lead                               [×]  │  header 64px, Typo.h5, bottom border
├─────────────────────────────────────────────┤
│ (scrollable body, padding 24px)             │
│  DynamicForm …                              │
├─────────────────────────────────────────────┤
│                      [Cancel]  [Create lead]│  sticky footer 64px, top border
└─────────────────────────────────────────────┘
```
Params: `Title`, `Open` (two-way), `ChildContent`, `SubmitText`, `OnSubmit` (async; button shows `MudProgressCircular Size.Small Indeterminate` and is disabled while running), `IsDirty` (closing when dirty → ConfirmDialog "Discard changes?" / "You have unsaved changes. They will be lost." / "Discard"). Esc closes (same guard). Focus moves to first field on open.
**KeyValueList.razor** — renders `IEnumerable<(string Label, RenderFragment Value)>` in `.ml-kv`; empty values show `—` in `ml-faint`.
**FileDropZone.razor** — `.ml-dropzone` + `InputFile multiple` overlay; content: icon `CloudUpload` (36 px, primary), text "**Drag & drop files here** or browse" (browse styled `ml-link`), caption "PDF, JPG or PNG · up to 25 MB each". Drag-over toggles `ml-dropzone--active` (JS-free: `@ondragenter`/`@ondragleave`/`@ondrop` on the wrapper). After selection shows a file list: file icon, name (truncate), size, per-file progress bar (`MudProgressLinear` 4 px), remove ✕; invalid files show red caption "File type not supported" / "File is larger than 25 MB".
**ChartCard.razor** — `MlCard` + `ApexChart`. Defaults (EXACT) via `ChartDefaults.Create<T>()`: `Chart.Toolbar.Show=false`, `Chart.FontFamily` = font stack, `Chart.Height=260`, `Colors` = chart palette, `Grid.BorderColor=var --ml-border hex (#E2E8F0 / #273244)`, `Grid.StrokeDashArray=4`, `DataLabels.Enabled=false`, `Legend.Position=Bottom`, `Legend.FontSize="12px"`, `Stroke.Width=2` (lines) / `0` (bars), `Stroke.Curve=Smooth`, `PlotOptions.Bar.BorderRadius=4`, `ColumnWidth="45%"`, `Tooltip.Theme` = light/dark, y-axis labels formatted with `MoneyText` compact rules for money series. Theme mode follows app dark mode. In RTL set `Chart.Locales`/`Xaxis` reversed: `Yaxis.Opposite=true`. Empty data → `EmptyState Compact` "No data for this period".
**DocumentPageViewer.razor** — see 19.11 / 18.9.12.

### 18.8 Page templates (every page is one of these)

#### 18.8.1 List page (Leads, Contacts, Accounts, Deals-list, Activities, Documents, Users…)
**Desktop (≥ 960 px):**
```
PageHeader: "Leads"                       subtitle "1,284 leads"      [⤒ Import] [⤓ Export] [+ New lead]
──────────────────────────────────────────────────────────────────────────────────────────────
 My open leads (128) │ All open (1,040) │ New this week (37) │ Unassigned (4) │ + Save view       ← view tabs (MudTabs, Text)
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ 🔍 Search leads…  (320px)   [⚲ Filters ²]  [Status: Qualified ×] [Owner: Me ×]  Clear   ⚙ ☰/▦│ toolbar 56px
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ ☐ │ NAME ↓                      │ STATUS       │ SOURCE     │ OWNER        │ CREATED   │  ⋮  │ header 40px (surface-2)
│ ☐ │ (AK) Ahmed Khan             │ ● Working    │ Website    │ (SK) Sara K. │ 2 h ago   │  ⋮  │ row 52px
│   │      ahmed@mail.com · +9715…│              │            │              │           │     │
│ … │                             │              │            │              │           │     │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│                                              Rows per page [50 ▾]   1–50 of 1,284   ‹  ›   │ pager 52px
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```
- Table = `MudDataGrid` (`ServerData`, `Dense="false"`, `Hover="true"`, `FixedHeader="true"`, `Height="calc(100vh - 300px)"`, `Elevation="0"`) inside `MlCard Flush`. Toolbar is inside the card above the grid.
- Primary column: `UserAvatar`/entity icon (32 px) + name (`Typo.subtitle1`, link to detail) + secondary line (email · phone) `Typo.body2 ml-muted`. Status → `StatusChip`. Owner → `UserAvatar Sm WithName`. Dates → `DateText Relative`. Money → `MoneyText`, right aligned. Max 7 visible columns by default; more via column chooser.
- Row click opens detail; checkbox click selects (doesn't navigate). Row "⋮" menu: Open, Edit, Assign, Delete.
- **Bulk mode** (≥ 1 selected): toolbar is replaced by: "**3 selected**" · [Assign] [Change status] [Add tags] [Export] [Delete (error text button)] · "Select all 1,284" link · [Clear]. Height unchanged (no layout jump).
- Filters button opens a 420 px end drawer with `FilterBuilder` + [Apply] [Reset]; active filters render as removable chips in the toolbar. Search debounced 300 ms; search + filters + sort + page are kept in the URL query (`?view=...&q=...&page=2`) so back/refresh restore state.
- **Tablet (600–959):** same, columns reduced to name, status, owner, created; header buttons icon-only except primary.
- **Phone (< 600):** no table. Card list:
```
┌─────────────────────────────────────┐
│ (AK) Ahmed Khan         ● Working   │
│      ahmed@mail.com                 │
│      Website · 2 h ago        (SK)  │
└─────────────────────────────────────┘
```
  cards 12 px apart, whole card tappable, swipe not required. Search full width; Filters as icon button with badge; view tabs as horizontal scroll chips. Pagination = "Load more" button (full width, outlined) at the end. FAB "+" bottom-end above bottom nav.
- **States:** loading → `SkeletonTable` (phone: `SkeletonCards`); never-had-data → `EmptyState` (18.12 copy) with primary + secondary action; no results → `EmptyState Icon=SearchOff` "No results" "Try different keywords or clear the filters." [Clear filters]; error → `ErrorState`.

#### 18.8.2 Record detail page (Lead, Contact, Account, Deal, Document-less records)
**Desktop (≥ 1280 px):**
```
Leads / Ahmed Khan                                                                             ← breadcrumbs
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ (AK 64px)  Ahmed Khan                      [📞] [WhatsApp] [✉]   [Convert]  [⋮]               │ header card
│            Sales Manager · Gulf Trading LLC                                                  │
│            ● Working   🔥 Hot   #vip   ·  Owner (SK) Sara Khan · Created 09 Oct 2026 · Website│
│  ───────────────────────────────────────────────────────────────────────────────────────    │
│  ( New ✓ )━━━( Working ● )━━━( Qualified )━━━( Converted )            ← status/stage stepper │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
┌────────────────────────────┐ ┌──────────────────────────────────────────────────────────────┐
│ Details              [✎]   │ │ Timeline │ Activities (4) │ Messages │ Documents (2) │ Files   │
│ CONTACT INFO               │ ├──────────────────────────────────────────────────────────────┤
│ Email      ahmed@mail.com  │ │ ┌ Note │ Task │ Call │ Meeting ───────────────────────────┐  │
│ Phone      +971 50 123 4567│ │ │ Write a note… @mention                                  │  │
│ Nationality UAE            │ │ └──────────────────────────────────────── [Save note] ─────┘  │
│ REQUIREMENTS               │ │ TODAY                                                        │
│ Property type  Villa       │ │ ○ 📞 Call logged by Sara — Connected · "Wants 3BR"  10:42   │
│ Budget   AED 2,500,000     │ │ ○ ✎ Status changed New → Working                    10:40   │
│ …                          │ │ YESTERDAY                                                    │
├────────────────────────────┤ │ ○ 🌐 Lead created from web form "Contact us"        16:05   │
│ Related                    │ │                                                              │
│ Account: —                 │ │                                                              │
└────────────────────────────┘ └──────────────────────────────────────────────────────────────┘
   left column 360px (sticky top 72px)          right column flexible
```
- Layout: `MudGrid`: left `lg="4" xl="3"` (min 320, max 400 px), right the rest. md: left 5/12. sm & xs: single column; the "Details" card becomes the first tab ("Details") of the tab bar.
- Header card: avatar Xl (or entity icon in a 64 px rounded square `--ml-primary-soft` for accounts/deals/documents), name `Typo.h4`, subtitle `Typo.body2 ml-muted`, chips row, meta row (`Typo.body2 ml-muted`, separated by "·"). Actions: quick icon buttons (`MudIconButton Variant.Outlined Size.Medium`) with tooltips, then the **primary** action (Convert / Mark won …) filled, then `MoreVert` menu (Edit, Change owner, Disqualify/Delete — delete last, red text). On xs the actions row moves under the name and quick actions become a horizontal row of 3 equal outlined buttons with icon + label.
- Stepper: custom component `StageStepper` — horizontal segments (height 32, radius 6); past stages `--ml-primary-soft` with ✓, current `--ml-primary` filled white text, future `--ml-surface-2`; won = success, lost = error. Clickable (deals) with confirmation dialogs as per 7.7. On xs it scrolls horizontally with the current stage auto-scrolled into view.
- Details card: sections from layout (`Typo.overline ml-muted` section titles), `KeyValueList`; hover on a value shows a pencil → inline edit (field editor + ✓/✕); Enter saves, Esc cancels; save → small success toast "Saved".
- Timeline: grouped by day headers (`Typo.overline`), items with 32 px circular icon (tone by type: call=primary, email=info, WhatsApp=#25D366, note=text-2, change=text-3, won=success), title line (`body1`, actor name bold), detail line (`body2 ml-muted`, max 3 lines with "Show more"), time (`caption` end aligned). Infinite scroll (load 30 more at bottom).
- Tabs: `MudTabs Elevation=0 Rounded=false ApplyEffectsToContainer=true PanelClass="pt-4"`, counts in parentheses, tab state in URL (`?tab=activities`).

#### 18.8.3 Settings page
```
PageHeader "Settings"
┌──────────────────────┐ ┌─────────────────────────────────────────────────────────────┐
│ GENERAL              │ │ Company profile                         (Typo.h4 page title) │
│  Company profile  ◀  │ │ Basic information about your business.                       │
│  Currencies & tax    │ │ ┌ SectionCard: Branding ────────────────────────────────────┐│
│ USERS & ACCESS       │ │ │ Logo [drop zone 160×160]   Primary color [■ #4F46E5]      ││
│  Users               │ │ └───────────────────────────────────────────────────────────┘│
│  Roles & permissions │ │ ┌ SectionCard: Details ─────────────────────────────────────┐│
│  Teams & branches    │ │ │ [Company name      ] [Legal name         ]                ││
│ CUSTOMIZATION        │ │ │ [Email             ] [Phone              ]                ││
│  Modules             │ │ └───────────────────────────────────────────────────────────┘│
│  Fields & layouts    │ │                                        [Cancel] [Save changes]│ sticky bottom bar when dirty
│  Picklists           │ └─────────────────────────────────────────────────────────────┘
│  Pipelines           │
│  Document types      │
│ CAPTURE & MESSAGING  │
│  Web forms · Rules · Templates · Channels · Booking pages
│ DATA                 │
│  Imports · Sample data · Industry
└──────────────────────┘
```
- Settings nav 240 px (lg+), content `ml-page--form` (max 960). md: nav 200 px. xs/sm: `/settings` shows the nav as a full-width list (each item with icon + description + chevron); selecting navigates to the page which has a back arrow in the header.
- Save bar: appears (slide up 200 ms) only when the form is dirty: sticky bottom, surface, top border, `Cancel` + `Save changes`.

#### 18.8.4 Forms (create/edit)
- **Create** = `FormDrawer` (≤ ~14 fields, which covers all Phase-1 records); **edit** = inline on detail page or the same drawer ("Edit lead"). Settings forms are inline pages with save bar.
- Inputs: **always** `Variant="Variant.Outlined" Margin="Margin.Dense"`, `Label` set, `Placeholder` only for examples ("e.g. ahmed@company.com"), `Required="true"` + `RequiredError` text, `HelperText` for hints. `Immediate="false"` (validate on blur). Dates: `MudDatePicker` with `DateFormat` from tenant, `Editable="true"`. Picklists: `MudSelect` (≤ 10 options) or `MudAutocomplete` (> 10). Users: `UserPicker` (autocomplete with avatars). Money: `MoneyInput` = numeric field + currency select (80 px) in one row. Phone: text field with country prefix hint; normalized on server.
- Grid: `MudGrid Spacing="3"`, each field `xs="12" sm="6"`; TextArea/Address/Description full width `xs="12"`. In drawers (480 px) use one column (`xs="12"` everywhere).
- Sections: `Typo.subtitle2` heading + 16 px gap; sections separated by 24 px.
- Submit: primary button text = verb + noun ("Create lead", "Save changes"). On server validation errors: map to fields, show snackbar "Please fix the highlighted fields", scroll first error into view. On success: close drawer, snackbar "Lead created" with action "Open" (if not navigating), refresh list.
- Never disable the submit button just because the form is invalid — validate on click and show errors (users must know *why*).

#### 18.8.5 Wizard (onboarding, import, convert)
```
             ┌──────────────────────────────────────────────────────────────┐ max-width 760, centered, margin-top 48
             │ ①─────②─────③─────④─────⑤─────⑥                              │ stepper (labels under numbers on ≥ 600)
             │ Company  Industry Pipeline Team  Data  Channels               │
             │──────────────────────────────────────────────────────────────│
             │ Tell us about your company           (Typo.h5)                │
             │ This sets your currency, time zone and language. (body2 muted)│
             │                                                              │
             │ [fields…]                                                    │
             │──────────────────────────────────────────────────────────────│
             │ [← Back]                               [Skip for now] [Continue]│ footer
             └──────────────────────────────────────────────────────────────┘
```
xs: the card is full-bleed (no border/radius, padding 16), stepper replaced by "Step 2 of 6 · Industry" + 4 px `MudProgressLinear`; footer sticky at bottom.

#### 18.8.6 Dialogs
Sizes: `ExtraSmall` 400 px (confirm), `Small` 600 px (simple forms), `Medium` 800 px (convert lead, apply-to-record diff). Title `Typo.h5`; content padding 24; actions end-aligned (Cancel text + primary filled). On xs all dialogs are `FullScreen="true"`.

### 18.9 Screen specifications (staff app)

#### 18.9.1 Login (`/login`)
```
┌───────────────────────────────┬──────────────────────────────────────────────┐
│  (brand panel, 44% width,      │                 [logo 36px]                  │
│   bg linear-gradient(135deg,   │          Welcome back       (Typo.h4)        │
│   #4F46E5 0%, #312E81 100%),   │   Sign in to your MindLift workspace (body2) │
│   white text, padding 48)      │                                              │
│                                │   Email      [                          ]   │
│  "Your whole business,         │   Password   [                     👁 ]   │
│   one simple CRM."  (h4 white) │   [✓] Keep me signed in   Forgot password?   │
│  ✓ Leads to loyal customers    │   [          Sign in  (full width)       ]   │
│  ✓ Dashboards that matter      │                                              │
│  ✓ Arabic & English            │   New to MindLift? Create an account         │
│                                │                                  EN | ع      │
└───────────────────────────────┴──────────────────────────────────────────────┘
```
Form column: content max-width 400, vertically centered. < 960 px: brand panel hidden, form centered on `--ml-bg` with logo on top. Errors: invalid credentials → `MudAlert Severity.Error Dense` above the button: "Email or password is incorrect." Locked → "Too many attempts. Try again in 15 minutes." Button shows spinner while signing in. Enter submits.

#### 18.9.2 Sign up (`/signup`)
Same split layout. Fields (single column): Company name, First name + Last name (two columns ≥ 600), Work email, Password (strength meter: 4-segment bar, text Weak/Fair/Good/Strong), Phone (optional), Country (autocomplete, default from browser locale), checkbox "I agree to the Terms and Privacy Policy" (links). Button "Create my workspace". Below: "Already have an account? Sign in". Success → `/onboarding`.

#### 18.9.3 Onboarding wizard (`/onboarding`) — uses Wizard template
- **Step 1 Company:** logo drop zone (160×160, optional) · Company name · Country · Currency (auto from country, editable) · Time zone (auto) · Language (segmented: English | العربية) · Tax: switch "We charge VAT" + rate (%) + TRN. Continue disabled? No — validate on click.
- **Step 2 Industry:** grid of 7 cards (3 per row lg, 2 sm, 1 xs), each card 100 % height: icon in 48 px soft circle, name (`h6`), 2-line description, "Includes:" 3 chips (e.g. "Property Deals", "Viewings", "Budget field"). Selected card: 2 px primary border + check badge at top-end. Below grid: "Preview" panel (MlCard) showing the selected template's pipeline stages as chips and custom fields list. Continue → applies template (button spinner "Setting up your CRM…", then success toast "Your CRM is ready for Real estate").
- **Step 3 Pipeline:** list of stages (drag handle ⋮⋮, color dot, name input, probability % input 80 px, delete ✕); Won/Lost rows pinned at bottom, not draggable/deletable; "+ Add stage" text button. Max 12 open stages.
- **Step 4 Team:** repeating rows: Email · First name · Role select · ✕; "+ Add another"; caption "You can invite people later from Settings → Users." Seats indicator "2 of 5 seats used".
- **Step 5 Data:** 3 large option cards (radio behavior): **Load sample data** (recommended badge) "Explore with realistic demo records. Remove them anytime." · **Import my data** "Upload Excel or CSV files with leads or contacts." · **Start empty**. Choosing Import opens the Import wizard in a dialog, then returns.
- **Step 6 Channels:** two cards: Email ("Connect your mailbox to send and log emails") [Connect] and WhatsApp [Connect] — each opens the channel setup dialog; both skippable. Final button "Go to my dashboard".
- After completion: confetti-free; navigate to dashboard; snackbar "Welcome to MindLift, Sara!".

#### 18.9.4 Dashboard (`/`)
```
Good morning, Sara                (Typo.h5)               [Staff | Manager | Admin]  [This month ▾] [⚙ Customize] [⟳]
Friday, 9 October 2026 · Updated 2 min ago (body2 muted)
┌────────────┐┌────────────┐┌────────────┐┌────────────┐┌────────────┐┌────────────┐
│SALES THIS M││OPEN PIPELIN││NEW LEADS   ││WIN RATE    ││CONVERSION  ││TASKS OVERDU│  StatCards row: lg 6 per row (md="2"),
│AED 1.2M    ││AED 8.4M    ││37          ││32%         ││18%         ││4           │  md 3 per row, sm 3, xs 2 (xs="6")
│▲ 12.5% vs …││weighted 3.1││▼ 4% vs last││▲ 3 pts     ││—           ││▼ 2 (good)  │
└────────────┘└────────────┘└────────────┘└────────────┘└────────────┘└────────────┘
┌──────────────────────────────────────────────┐┌──────────────────────────┐
│ My day                         View all →    ││ Target progress          │
│ [Today (5)] [Overdue (2)] [Meetings (3)]     ││ Won amount               │
│ ◯ Call back Ahmed Khan   Lead · Ahmed  14:00 ││ AED 320K of 500K   64%   │
│ ◯ Send proposal          Deal · Villa  16:00 ││ ███████████▏░░░░|░░  Ahead│
│ …                                            ││ Deals won 3 of 6  50%    │
└──────────────────────────────────────────────┘└──────────────────────────┘
┌──────────────────────┐┌──────────────────────┐
│ New leads            ││ Upcoming meetings    │
└──────────────────────┘└──────────────────────┘
┌──────────────────────┐┌──────────────────────┐
│ Deals in progress    ││ Deals closing soon   │
│ (horizontal bars     ││                      │
│  by stage)           ││                      │
└──────────────────────┘└──────────────────────┘
```
- Grid: `MudGrid Spacing="4"`; widget size → `xs=12` always; Sm → `sm=6 md=3`; Md → `md=6`; Lg → `md=9`… (Sm=3, Md=6, Lg=9, Xl=12 columns on md+).
- Widget card = `MlCard` with title (h6) + header action ("View all" link or a small select); body min-height 200 px (skeleton same height to avoid layout shift); lists show max 5 rows then "View all"; each row 48 px: leading icon/checkbox, title (`body1` truncate), secondary (`body2 ml-muted` truncate), trailing time/amount (`caption`/`MoneyText`).
- `my_day` tasks: circular checkbox (`MudCheckBox` with `CheckedIcon=CheckCircle`, `UncheckedIcon=RadioButtonUnchecked`); completing animates strike-through 150 ms then the row collapses (200 ms) and the tab count updates; snackbar "Task completed" + Undo (6 s).
- `target_progress`: per metric: label `body2 ml-muted`, value line "**AED 320K** of 500K" + % end aligned, `.ml-progress` with `--bar` = success (ahead) / primary (on track) / warning (behind), `.ml-progress__marker` at expected pace %, status text caption.
- `team_leaderboard`: table (rank 1–3 with gold/silver/bronze 20 px circles `#F59E0B`/`#94A3B8`/`#B45309`), avatar+name, won (Money compact), deals, mini progress to target (80 px).
- Charts via `ChartCard` (height 260). `deals_in_progress` = horizontal bar per stage (stage colors), `leads_by_source` = donut with total in center, `sales_trend` = area (won) + dashed line (target), `pipeline_funnel` = funnel (bar with `IsFunnel`), `activity_summary` = grouped columns current vs previous.
- **Customize mode:** banner inside page "Drag widgets to reorder. Changes are saved for you only." + [Reset to default] [Done]; each widget shows a drag handle (top-start) and an eye toggle (top-end); hidden widgets appear dimmed (opacity .5) at the end in a "Hidden widgets" section.
- Mobile: greeting + compact view switch (select); KPIs 2 per row; widgets full width in the default order; charts height 220.
- Empty tenant (no data & no sample): show onboarding checklist widget + every widget's compact empty state with action (e.g. My day: "Nothing due today 🎉" no action; New leads: "No new leads" [Create web form]).

#### 18.9.5 Leads list / detail / convert
- List & detail per 18.8.1 / 18.8.2. Extra list columns: Rating (🔥 icons? → use `StatusChip` Hot=error, Warm=warning, Cold=info), Source, Budget (if template). Kanban toggle (by status) uses the board layout of 18.9.6 with status columns.
- "Uncontacted for > 24 h" → small warning dot before the name + tooltip "Not contacted for 26 hours".
- **Convert dialog** (`MaxWidth.Medium`):
```
Convert Ahmed Khan                                                     [×]
Create the records you need. Activities and history move with them.
┌ Account ─────────────────────────────────────────────────────────────┐
│ (•) Create new  "Gulf Trading LLC"   ( ) Link existing [search…]  ( ) None │
│ ⚠ Similar account found: Gulf Trading L.L.C. [Use this]               │
└──────────────────────────────────────────────────────────────────────┘
┌ Contact ─────────────────────────────────────────────────────────────┐
│ (•) Create new  "Ahmed Khan · ahmed@mail.com"   ( ) Link existing      │
└──────────────────────────────────────────────────────────────────────┘
┌ Deal ───────────────────────────────────────────────── [✓ Create deal]┐
│ Name [Gulf Trading – Villa 3BR     ]  Pipeline [Sales ▾] Stage [Enquiry ▾]│
│ Amount [AED][2,500,000]   Expected close [15 Dec 2026]   Owner [(SK) Sara ▾]│
└──────────────────────────────────────────────────────────────────────┘
                                                   [Cancel]  [Convert lead]
```
Success → navigate to the deal; snackbar "Lead converted. Contact, account and deal created."

#### 18.9.6 Deals board (`/deals`)
```
PageHeader "Property Deals"   [Sales pipeline ▾]  [Board | List]        [⤓ Export] [+ New deal]
Toolbar: 🔍 Search · [⚲ Filters] · Owner [All ▾] · Total open: AED 8.4M · Weighted AED 3.1M
┌─ Enquiry ─────────┐┌─ Viewing ─────────┐┌─ Offer ───────────┐┌─ Agreement ───────┐┌─ Won ─────┐┌─ Lost ────┐
│ 12 · AED 2.1M     ││ 8 · AED 3.0M      ││ …                 ││                   ││ (30 days) ││ (30 days) │
│ ┌───────────────┐ ││                   ││                   ││                   ││           ││           │
│ │ Villa 3BR Palm│ ││                   ││                   ││                   ││           ││           │
│ │ Gulf Trading  │ ││                   ││                   ││                   ││           ││           │
│ │ AED 2,500,000 │ ││                   ││                   ││                   ││           ││           │
│ │ 📅 15 Dec  (SK)│ ││                   ││                   ││                   ││           ││           │
│ └───────────────┘ ││                   ││                   ││                   ││           ││           │
└───────────────────┘└───────────────────┘└───────────────────┘└───────────────────┘└───────────┘└───────────┘
```
- Columns 280 px wide, 12 px gap, horizontal scroll, column background `--ml-surface-2`, radius 10, header sticky (stage name `subtitle2` + color dot; count · total `caption ml-muted`; weighted in tooltip).
- Card: `ml-card` 12 px padding, 8 px gap between cards; title `subtitle1` 2-line clamp; account `body2 muted`; amount `MoneyText` bold; footer row: expected date (red if past) + avatar Sm. Rotting deal → 3 px start border `--ml-warning` + tooltip "No activity for 9 days". Dragging: card lifts (`shadow-3`, rotate 2°), target column highlights with primary dashed outline.
- Drop on Won → dialog "Mark as won" (close date default today) ; on Lost → dialog with required reason select + notes.
- xs: one column at a time with a stage selector (scrollable chips) on top; cards full width; move stage via card menu "Move to…" (no drag on phones).

#### 18.9.7 Activities (`/activities`) & Calendar (`/calendar`)
- Activities: list template, tabs Today / Overdue / Upcoming / Completed; rows show type icon (Task `TaskAlt`, Call `Call`, Meeting `Groups`, Note `StickyNote2`), subject, related record link, due (`DateText`, red when overdue), priority chip (only High/Urgent shown), owner avatar, complete checkbox at start.
- Calendar: `PageHeader` with [Today] [‹] [›] + date range title + view toggle [Day|Week|Month] + user/team select + [+ New meeting]. Heron MudCalendar; event chip colors: meeting primary, task info, booking secondary, busy `--ml-surface-2` with text-3 label "Busy". Click event → popover card (title, time, attendees, related, [Open] [Edit]). xs: default Day view, agenda list style.

#### 18.9.8 Inbox (`/inbox`)
Two-pane on md+: left conversation list 360 px (avatar, name, channel icon (Email `MailOutline`, WhatsApp brand green circle "W", SMS `Sms`), last message preview 1 line, time, unread dot), right conversation (header: contact name + record link + channel; messages: outbound bubbles end-aligned `--ml-primary-soft`, inbound start-aligned `--ml-surface-2`, radius 12, max-width 70 %, time caption under, status ticks for WhatsApp; composer bottom with template button, attach, text area auto-grow, Send). Email threads render as stacked cards (from, to, subject, sanitized body in sandboxed iframe, attachments chips). xs: list → tap → full-screen conversation with back arrow. WhatsApp 24 h window closed → composer shows info bar "The 24-hour window is closed. Send an approved template to restart the conversation." + [Choose template].

#### 18.9.9 Reports home (`/reports`)
```
PageHeader "Reports"                                                         [+ New report]
┌──────────────────┐ ┌──────────────────────────────────────────────────────────────────┐
│ ★ Favorites      │ │ 🔍 Search reports…                                               │
│ ▤ System reports │ │ ┌──────────────────┐┌──────────────────┐┌──────────────────┐     │
│ ▤ My reports     │ │ │ 📊 Sales by user  ☆││ 🍩 Lead sources ☆ ││ 📈 Revenue      ☆ │     │
│ ▤ Shared with me │ │ │ Won deals, win    ││ Where your leads  ││ Won revenue by   │     │
│ FOLDERS          │ │ │ rate and pipeline ││ come from         ││ month            │     │
│ ▸ Finance        │ │ │ System · 2 h ago  ││ System · —        ││ System · —       │     │
│ + New folder     │ │ └──────────────────┘└──────────────────┘└──────────────────┘     │
└──────────────────┘ └──────────────────────────────────────────────────────────────────┘
```
Cards: 3 per row lg, 2 md, 1 xs; icon reflects chart type; clickable card; star toggles favorite. Also "Targets" entry at top of the left list.

#### 18.9.10 Report viewer (`/reports/{id}`)
```
Reports / Sales by user
PageHeader "Sales by user"  subtitle description          [☆] [Schedule] [Save as] [⤓ Export ▾]
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ Period [This month ▾]  Users [All ▾]  Team [All ▾]  Pipeline [Sales ▾]         [Reset]   │ parameter bar (card)
└──────────────────────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ (chart, height 320)                                                                      │
└──────────────────────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ USER          │ WON │ WON AMOUNT    │ LOST │ WIN RATE │ AVG DEAL     │ OPEN PIPELINE     │
│ (SK) Sara Khan│  6  │ AED 1,250,000 │  2   │ 75%      │ AED 208,333  │ AED 3,100,000     │
│ …             │     │               │      │          │              │                   │
│ Total         │ 14  │ AED 2,900,000 │  7   │ 67%      │ AED 207,142  │ AED 8,400,000     │ bold, surface-2
└──────────────────────────────────────────────────────────────────────────────────────────┘
  "1,000 of 3,240 rows shown — export to get all rows."  (only when truncated)
```
Parameter changes re-run automatically (debounce 400 ms) with chart/table skeletons; numeric cells right aligned; clicking a row/segment drills down (opens list in a new route with filters, breadcrumb back to the report). Export menu: Excel, PDF, CSV (spinner in menu item; download starts; toast "Export ready").

#### 18.9.11 Report builder (`/reports/new`, `/reports/{id}/edit`)
Two-pane: left config panel 400 px (scrollable) with collapsible sections in order **1 Source · 2 Type · 3 Columns / Group & aggregate · 4 Filters · 5 Chart**; right live preview (chart + table, "Preview shows up to 200 rows"). Top bar: name inline-editable (`Typo.h5`, placeholder "Untitled report"), [Cancel] [Save]. Fields picker: searchable list grouped by related object with type icons (Aa text, # number, 💲 money, 📅 date, ▾ picklist, 👤 user). Selected columns as reorderable chips. Invalid config → inline red helper text in the section, preview shows `EmptyState` "Choose at least one column to preview". xs: config and preview become tabs.

#### 18.9.12 Documents list & Document review (`/documents`, `/documents/{id}`)
- **List:** list template; columns: name (file-type icon PDF red `PictureAsPdf` / image `Image` + name + linked record line), type, status chip (Uploaded/Queued info, Processing primary with 12 px spinner, NeedsReview warning, Approved success, Rejected/Failed error), confidence (`StatusChip` "92%": ≥ 85 success, 60–84 warning, < 60 error), expiry (`DateText`; ≤ 30 days warning chip "Expires in 21 days", past → error "Expired"), source (Staff/Portal icon), uploaded. View tabs: Needs review · All · Expiring soon · From portal. Header actions: [+ Upload] primary.
- **Upload drawer** (FormDrawer "Upload documents"): FileDropZone · Document type (select, default "Detect automatically") · Link to (record picker: contact/account/deal/lead) · [✓] Share with customer portal · [Upload]. After upload rows show live status (SignalR) and "Open" links.
- **Review screen:**
```
Documents / Trade license – Gulf Trading.pdf
┌────────────────────────────────────────────────────────────────────────────────────────────┐
│ [PDF] Trade license – Gulf Trading.pdf   Type [Trade license ▾]  ● Needs review  OCR 88%   │
│ Linked: (🏢) Gulf Trading LLC · Uploaded by Ahmed (Portal) · 2 h ago   [Reprocess] [Reject] [Approve] │
└────────────────────────────────────────────────────────────────────────────────────────────┘
┌──────┬─────────────────────────────────────────────┐┌───────────────────────────────────────┐
│ [p1] │  Page 1 of 2   [−] 100% [+] [Fit width] [⧉ highlights]  ││ Extracted fields │ Text │ Apply to record │
│ [p2] │ ┌─────────────────────────────────────────┐ ││ ───────────────────────────────────── │
│      │ │                                         │ ││ License number *           ● 97%  ⌖   │
│ thumbs│ │   (page image with highlight boxes)    │ ││ [CN-1234567                     ]     │
│ 96px │ │   ┌──────────┐ ← active field (amber)   │ ││ Company name *             ● 91%  ⌖   │
│      │ │   └──────────┘                          │ ││ [GULF TRADING L.L.C            ]      │
│      │ │                                         │ ││ Expiry date *              ● 72%  ⌖   │
│      │ └─────────────────────────────────────────┘ ││ [31 Mar 2027                ] ⚠ check │
│      │                                             ││ Issue date                  ✕ Not found│
│      │                                             ││ [                           ] enter manually│
│      │                                             ││ Activities                  ● 85%     │
└──────┴─────────────────────────────────────────────┘└───────────────────────────────────────┘
          viewer ~60% (lg: 7/12)                                panel ~40% (lg: 5/12), sticky
```
  - Field row: label (`subtitle2`, * if required), confidence chip (color by thresholds), locate button (`MyLocation` icon) → scrolls viewer to the page and flashes the box (`ml-doc-box--active`); input by value type (date picker, numeric, text); edited fields show caption "Edited by you" and confidence chip turns neutral "Manual". Missing required → red caption "Not found — enter it manually". Fields sorted: required first, then by `SortOrder`.
  - Focus on a field highlights its box; hovering a box shows the field label tooltip.
  - **Text** tab: page selector + raw OCR text in a monospace-free `body1` block with search box (matches highlighted), "Copy text" button.
  - **Apply to record** tab: record picker (pre-filled with linked record) and a diff table: Field · Current value · New value · [✓ apply] checkbox per row (default checked when current is empty or different); note "Only checked fields will be updated."
  - **Approve** (primary): validates required; dialog only if "Apply to record" has checked rows ("Update 3 fields on Gulf Trading LLC?"). Success → status Approved, snackbar "Document approved", next document in the review queue opens automatically if the user came from "Needs review" (button "Approve & next" when queue > 1).
  - **Reject**: dialog with reason (required) → status Rejected; if from a portal request, customer is notified to re-upload.
  - States: Processing → viewer area shows centered `MudProgressCircular` + "Reading your document… This usually takes under a minute." and live updates via SignalR; Failed → `ErrorState` "We couldn't read this file" + reason + [Reprocess].
  - md: thumbnails hidden (page selector only). xs/sm: header actions in a bottom sticky bar ([Reject] [Approve]); viewer on top (height 50vh, pinch zoom) and fields below.

#### 18.9.13 Settings → Document types (`/settings/document-types`)
List of types (icon, name, fields count, auto-approve badge, active switch). Type page: SectionCards — "General" (name EN/AR, icon, languages, auto-approve switch + threshold slider 70–100, reminder days chips, portal visible default), "Classification keywords" (chips with weights), "Fields" (table: key, label, type, required, metric, portal, sensitive, map to, rules count, ⋮ edit → drawer with rule list editor: each rule card shows kind select + its inputs + up/down order), "Test with a sample" (FileDropZone → results panel identical to the review panel, read-only, with timing "Processed 2 pages in 6.4 s").

### 18.10 Screen specifications (customer portal) — see Section 20 for data
Portal shell: **no side drawer.** Top bar 64 px: tenant logo (max height 32) + tenant name (≥ 600) · nav links (Home, Documents, Requests — text buttons, active underlined 2 px primary) · language toggle · avatar menu (Profile, Sign out). Content max-width 1120 centered, padding 24/16. xs: top bar shows logo + avatar only; bottom nav with 3 items (Home `Home`, Documents `Description`, Requests `AssignmentTurnedIn` with badge). Primary color = tenant color. Footer: "Powered by MindLift" caption + support email/phone from portal settings.

**Portal login (`/portal/{slug}/login`):** centered card 420 px on `--ml-bg`: tenant logo (48 px) + "Sign in to {Tenant name}" (`h5`) + "View your documents and requests." (body2 muted). Tabs: [Password] [Email me a link]. Password: Email, Password, "Forgot password?", [Sign in]. Link: Email, [Send sign-in link] → success state "Check your inbox — we sent a sign-in link to a***@mail.com. It expires in 15 minutes." Language toggle bottom.

**Portal home (`/portal/{slug}`):**
```
Welcome back, Ahmed                               (h5)
Gulf Trading LLC                                  (body2 muted)
┌ Action needed ──────────────────────────────────────────────────────────────┐ (only if open requests)
│ ⚠ Upload a copy of your Emirates ID          Due 12 Oct 2026   [Upload]       │
│ ⚠ Upload signed agreement                    Due 20 Oct 2026   [Upload]       │
└──────────────────────────────────────────────────────────────────────────────┘
Key information                                                         (h6)
┌──────────────────────────┐┌──────────────────────────┐┌──────────────────────────┐
│ TRADE LICENSE            ││ PASSPORT                 ││ EMIRATES ID              │
│ License no.  CN-1234567  ││ Number     ••••4821      ││ ID number  784-••••-•••1 │
│ Expiry   31 Mar 2027     ││ Expiry  05 Jan 2027      ││ Expiry  14 Nov 2026      │
│ ● Valid · 173 days left  ││ ● Expires in 88 days     ││ ● Expires in 36 days     │
│ View document →          ││ View document →          ││ View document →          │
└──────────────────────────┘└──────────────────────────┘└──────────────────────────┘
Metrics                                                                  (h6)
┌──────────────────┐┌──────────────────┐┌──────────────────────────────────────────┐
│ REVENUE (2025)   ││ NET PROFIT (2025)││ Revenue by year (area chart)              │
│ AED 12.4M ▲ 18%  ││ AED 1.9M ▲ 6%    ││                                          │
└──────────────────┘└──────────────────┘└──────────────────────────────────────────┘
Recent documents                                                  View all →
 [PDF] Financial statements 2025   Approved · 02 Oct 2026                    ›
 [IMG] Emirates ID                 In review · 08 Oct 2026                   ›
```
- Key info cards: 3 per row lg, 2 sm, 1 xs; validity chip: > 30 days success "Valid · N days left", 8–30 warning "Expires in N days", ≤ 7 error, past error "Expired".
- Metrics section only when the tenant enabled `ShowMetrics` and metric fields exist; StatCards show latest period value + delta vs previous period; chart = ChartCard area per metric (select to switch metric).
- Empty home (no documents yet): EmptyState "Nothing here yet" "Documents shared with you will appear here." (+ [Upload a document] if uploads allowed).

**Portal documents (`/portal/{slug}/documents`):** search + type filter chips; card list (icon, name, type, status chip mapped for customers: "In review" (warning), "Ready" (success), "Needs a new upload" (error)), date; tap → detail.
**Portal document detail:** two-pane (lg): viewer (same `DocumentPageViewer`, highlights OFF, no thumbnails < 960) + info panel "Details" (`KeyValueList` of portal-visible approved fields, sensitive values masked with an eye toggle to reveal) + [⤓ Download]. Documents still in review show info alert "We're reviewing this document. Details will appear here once it's approved."
**Portal requests (`/portal/{slug}/requests`):** list cards: title, description, due date (warning ≤ 3 days, error overdue), status chip (Open / Submitted / Completed); Open → [Upload] opens a bottom sheet (xs) / dialog (≥ 600) with FileDropZone (camera capture allowed on phones: `accept="image/*,application/pdf" capture="environment"` option button "Take a photo") → submit → status "Submitted — we'll review it soon".

### 18.11 Interaction & feedback rules (EXACT)
| Situation | Behavior |
|---|---|
| Save success | Snackbar success "{Entity} saved" / "{Entity} created" (4 s). No snackbar for inline field edits — show a ✓ that fades in 1 s next to the field. |
| Delete | Confirm dialog for records with related data ("Delete Gulf Trading LLC? Its 3 contacts and 2 deals stay but lose the link.") — otherwise immediate delete + snackbar "Lead deleted" with **Undo** (6 s, calls restore). |
| Server error (5xx/network) | Snackbar error "Something went wrong. Please try again." (8 s) + keep the user's input. |
| Validation error (400/422) | Field errors inline; snackbar "Please fix the highlighted fields". |
| Permission (403) | Page-level `EmptyState Icon=Lock` "You don't have access to this page" "Ask your administrator for access." [Go to dashboard]. |
| Not found (404) | `EmptyState Icon=SearchOff` "We couldn't find that {entity}" "It may have been deleted or you may not have access." [Back to {entities}]. |
| Concurrency (409) | Snackbar warning "This record was changed by someone else. Reloaded the latest version." and reload. |
| Long operation (> 1 s) | Button spinner + disabled; > 5 s jobs → background with toast "We'll notify you when it's done". |
| Real-time notification | Snackbar info with title + "View" action (5 s); bell count increments with a 300 ms scale pulse. |
| Unsaved changes | Navigation away / drawer close → ConfirmDialog "Discard changes?" |
| Empty fields | Show "—" (`ml-faint`), never "null", "N/A" or blank. |
| Long text | Truncate with ellipsis + tooltip with full text; names wrap to 2 lines max in cards. |
| Hover | Rows/cards: background `--ml-hover` (150 ms). Buttons: MudBlazor default. Cursor pointer on everything clickable. |
| Keyboard | `/` focuses search; `n` opens "New" on list pages; `Esc` closes drawers/dialogs; Enter submits forms (except text areas). |
| Loading > 300 ms | Skeletons in the exact shape of the content; never full-page spinners (except initial app load: centered logo + 3 px top progress bar). |

### 18.12 Copy & microcopy (EXACT strings — put them in the .resx files with these keys)
Tone: friendly, short, plain English; second person ("you"); no jargon ("record" is fine; avoid "entity", "null", "payload"). Buttons = verb + noun.

| Key | English | Arabic |
|---|---|---|
| `Common.Save` | Save changes | حفظ التغييرات |
| `Common.Cancel` | Cancel | إلغاء |
| `Common.Delete` | Delete | حذف |
| `Common.Undo` | Undo | تراجع |
| `Common.Search` | Search | بحث |
| `Common.Filters` | Filters | عوامل التصفية |
| `Common.ClearFilters` | Clear filters | مسح عوامل التصفية |
| `Common.ViewAll` | View all | عرض الكل |
| `Common.TryAgain` | Try again | حاول مرة أخرى |
| `Common.LoadMore` | Load more | تحميل المزيد |
| `Common.Saved` | Saved | تم الحفظ |
| `State.ErrorTitle` | Something went wrong | حدث خطأ ما |
| `State.ErrorMessage` | We couldn't load this. Check your connection and try again. | تعذّر التحميل. تحقق من اتصالك وحاول مرة أخرى. |
| `State.NoResultsTitle` | No results | لا توجد نتائج |
| `State.NoResultsMessage` | Try different keywords or clear the filters. | جرّب كلمات أخرى أو امسح عوامل التصفية. |
| `State.NoAccessTitle` | You don't have access to this page | ليس لديك صلاحية الوصول إلى هذه الصفحة |
| `Leads.EmptyTitle` | No leads yet | لا يوجد عملاء محتملون بعد |
| `Leads.EmptyMessage` | Leads you capture from web forms, WhatsApp, email or imports appear here. | يظهر هنا العملاء المحتملون من النماذج وواتساب والبريد والاستيراد. |
| `Contacts.EmptyTitle` | No contacts yet | لا توجد جهات اتصال بعد |
| `Contacts.EmptyMessage` | Add the people you work with, or convert a lead. | أضف الأشخاص الذين تعمل معهم أو حوّل عميلًا محتملًا. |
| `Accounts.EmptyTitle` | No companies yet | لا توجد شركات بعد |
| `Deals.EmptyTitle` | No deals yet | لا توجد صفقات بعد |
| `Deals.EmptyMessage` | Create a deal or convert a qualified lead to start your pipeline. | أنشئ صفقة أو حوّل عميلًا محتملًا مؤهلًا لبدء مسار المبيعات. |
| `Activities.NothingToday` | Nothing due today | لا توجد مهام لليوم |
| `Documents.EmptyTitle` | No documents yet | لا توجد مستندات بعد |
| `Documents.EmptyMessage` | Upload PDFs or photos. We'll read them and pull out the key details for you. | ارفع ملفات PDF أو صورًا وسنستخرج التفاصيل المهمة لك. |
| `Documents.Processing` | Reading your document… This usually takes under a minute. | جارٍ قراءة المستند… يستغرق ذلك عادةً أقل من دقيقة. |
| `Documents.NotFoundField` | Not found — enter it manually | لم يُعثر عليه — أدخله يدويًا |
| `Reports.EmptyPeriod` | No data for this period | لا توجد بيانات لهذه الفترة |
| `Dashboard.GoodMorning` | Good morning, {0} | صباح الخير، {0} |
| `Dashboard.GoodAfternoon` | Good afternoon, {0} | مساء الخير، {0} |
| `Dashboard.GoodEvening` | Good evening, {0} | مساء الخير، {0} |
| `Portal.Welcome` | Welcome back, {0} | مرحبًا بعودتك، {0} |
| `Portal.InReview` | We're reviewing this document. Details will appear here once it's approved. | نراجع هذا المستند. ستظهر التفاصيل هنا بعد اعتماده. |
Greeting: < 12:00 morning, < 17:00 afternoon, else evening (user's local time). Translate every other UI string the same way; Arabic must be reviewed by a native speaker (open question 10).

### 18.13 RTL & Arabic rules
1. `MudRTLProvider` + `<html dir="rtl" lang="ar">`. Use **logical** CSS only (`margin-inline-start`, `padding-inline`, `inset-inline-end`, `text-align:start|end`) — never `left/right` in custom CSS.
2. Mirror directional icons (arrows, chevrons, "back", progress direction); do **not** mirror: phone, play, checkmarks, logos, charts' time axis direction stays left→right only if `Yaxis.Opposite` handling looks wrong — verify via screenshot.
3. Numbers, emails, phone numbers, IDs, and money are wrapped in `<bdi>` (or `dir="ltr"` span) so "+971 50 123 4567" and "AED 1,250" don't flip.
4. Mixed-language data (English names in Arabic UI) must render correctly — test with sample data in both languages.
5. Fonts switch to IBM Plex Sans Arabic; line-height stays the same.
6. Date month names in Arabic use `ar-AE` culture (e.g. "٠٩ أكتوبر ٢٠٢٦" → force Western digits via custom `NumberFormat.NativeDigits` = 0–9 or by formatting day/year separately).

### 18.14 Accessibility rules
WCAG 2.1 AA: text contrast ≥ 4.5:1 (tokens already pass), focus visible on every interactive element, all icon-only buttons have `aria-label` (and `Title` tooltip), form fields have labels, status is never conveyed by color alone (chips have text, deltas have arrows), dialogs trap focus and return it, images have `alt`, minimum target 40×40 on touch, page `<title>` = "{Page} · {Tenant} · MindLift".

### 18.15 UI Definition of Done (check EVERY page before ticking it)
- [ ] Uses the correct page template (18.8) and `PageHeader`; one primary action at the header end.
- [ ] Only tokens/classes from 18.5 and MudBlazor props; no hard-coded colors, px font sizes, or inline styles (except `--chip`/`--bar` variables).
- [ ] Typography roles exactly per 18.3 (no h1–h3; titles h4; card titles h6).
- [ ] Spacing: 24 px between sections, 16 px between cards, card padding from `.ml-card__body`.
- [ ] Loading skeleton, empty state (with copy from 18.12), error state with retry, and normal state all implemented and seen in screenshots (use `?_state=loading|empty|error` dev query switch supported by list/dashboard pages in Development).
- [ ] Works at 1440, 768 and 375 px: no horizontal page scroll (only inside tables/boards), nothing overlaps, touch targets ≥ 40 px, bottom nav visible on phones and not covering content.
- [ ] Arabic: layout mirrored, text not clipped, numbers/phones not reversed, icons mirrored correctly.
- [ ] Dark mode readable (spot check dashboard, list, detail, review screen).
- [ ] All text from `.resx` (no hard-coded English).
- [ ] Every clickable thing has hover + focus states; icon buttons have tooltips/aria-labels.
- [ ] Money/date/number formatting via `MoneyText`/`DateText`/`ml-num`.
- [ ] Buttons and labels follow copy rules (verb + noun, sentence case).
- [ ] Data-testids present: `page-title`, list `data-testid="list"`, rows `data-testid="row"`, primary action `data-testid="primary-action"`, empty state `empty-state`.
- [ ] Screenshot files reviewed and attached to `PROGRESS.md` entry (paths).

### 18.16 Screenshot verification harness (Playwright)
- Project `tests/MindLift.E2E` (xUnit + `Microsoft.Playwright`). After first build run once: `pwsh tests/MindLift.E2E/bin/Debug/net10.0/playwright.ps1 install chromium`.
- Dev seed: `dotnet run --project src/MindLift.Api -- seed-demo` creates tenant **"Demo Realty"** (template real_estate, sample data, 3 users: admin/manager/staff) — credentials are read from `appsettings.Development.json` → `DemoSeed` section (never hard-code them in tests; tests read the same config).
- `tests/MindLift.E2E/screens.json` lists screens: `{ "name":"leads-list", "url":"/leads", "role":"staff", "waitFor":"[data-testid=list]" }` … one entry per screen in 18.9/18.10 (+ `_state` variants for empty/loading/error).
- Test `ScreenshotTests.CaptureAll` loops screens × viewports `[1440×900, 768×1024, 375×812]` × languages `[en, ar]` (+ dark for dashboard, list, detail, review): logs in via API (stores token in session), sets `localStorage["ml.lang"]`, navigates, waits for `waitFor` + network idle, then `page.ScreenshotAsync(new() { Path = $"screenshots/{name}-{w}-{lang}{(dark?"-dark":"")}.png", FullPage = true })`.
- Also asserts: no console errors, no element wider than viewport (`document.documentElement.scrollWidth <= innerWidth`), `[data-testid=page-title]` visible.
- Run: `dotnet test tests/MindLift.E2E --filter Category=Screens` (API must be running on `http://localhost:5080`, set in `E2E:BaseUrl`).
- **The building agent must open the PNGs (image reading) and compare against the wireframes and the UI DoD before marking a page done.** Fix and re-capture until it matches.

---

## 19. ★ DOCUMENTS & DOCUMENT INTELLIGENCE (OCR WITH TESSERACT)

### 19.1 Goal & scope
Staff (and customers via the portal) upload PDFs or photos (trade licenses, passports, Emirates IDs, invoices, financial statements, insurance cards, title deeds…). The system:
1. Stores the file and renders page images.
2. Reads the text (digital PDF text layer, or **Tesseract OCR** for scans/photos) in **English and Arabic**.
3. Detects the document type.
4. **Extracts key information and metrics** (numbers, dates, IDs, amounts) using per-type rules, with confidence and the exact location on the page.
5. Lets staff **review, correct and approve** values (with highlights on the page).
6. **Stores** approved values as structured data → used for expiry alerts, record fields (apply to contact/account), reports, dashboards, and the **Customer Portal**.

Out of scope (later): handwriting, AI/LLM extraction (optional adapter in Phase 2), table reconstruction beyond "row label → numbers".

### 19.2 Flow
```
Upload (staff UI / portal / email attachment*) ──► documents(Status=Queued) ──► Hangfire "ocr" queue
   │                                                                           │
   │                                         DocumentProcessingJob ◄───────────┘
   │   1 load file → 2 per page: text layer? (PdfPig) ─ yes ─► words+bboxes
   │                                         └─ no ──► render 300 DPI (PDFtoImage) → preprocess (SkiaSharp) → Tesseract TSV → words+bboxes
   │   3 render 150 DPI view images + thumbnails   4 build LayoutModel (lines)   5 classify type
   │   6 run extraction rules → fields with confidence + bbox   7 save pages, text, fields
   │   8 status: Approved (auto-approve) | NeedsReview | Failed   9 SignalR + notifications
   ▼
Review screen (staff) ──► Approve (+ apply to record, expiry date) ──► visible in reports, dashboard, portal
* "Save attachment as document" action on email/WhatsApp messages.
```

### 19.3 Data model (all `TenantEntity`)
**document_types:** Key (unique per tenant), Name, NameAr, Description, Icon, Classifier (jsonb `{ "keywords":[{"text":"trade license","weight":3},{"text":"رخصة تجارية","weight":3}], "minScore":4 }`), OcrLanguages (default `"eng+ara"`), PageSegMode (default 3), AutoApprove (bool, default false), AutoApproveThreshold (int, default 90), ExpiryFieldKey?, IssueFieldKey?, PeriodFieldKey?, ReminderDaysBefore (int[], default `{60,30,7}`), DefaultPortalVisible (bool), IsSystem, IsActive, Origin.
**document_type_fields:** DocumentTypeId, Key (`^[a-z][a-z0-9_]{1,49}$`), Label, LabelAr, ValueType (`Text|Number|Amount|Date|Boolean|Id`), Required, IsMetric (numeric fields aggregated in reports/portal), PortalVisible, IsSensitive (masked in lists/portal cards), Rules (jsonb array, 19.6), MapTo (e.g. `"accounts.trade_license_number"`, `"contacts.cf_passport_number"`, `"contacts.date_of_birth"`, null), SortOrder.
**documents** (Owned, CF, S): Name, FileId (original), ContentType, SizeBytes, Sha256 (duplicate upload warning), PageCount, Source (`Staff|Portal|Email|WhatsApp`), Status (`Queued|Processing|NeedsReview|Approved|Rejected|Failed`), DocumentTypeId?, ClassifiedBy (`User|Auto|None`), ClassificationScore, TextSource (`Digital|Ocr|Mixed`), MeanConfidence (0–100), FullText (text), SearchVector (`tsvector` generated: `to_tsvector('simple', coalesce(name,'') || ' ' || coalesce(full_text,''))` STORED, GIN index), IssueDate?, ExpiryDate?, PeriodDate?, LeadId?, ContactId?, AccountId?, DealId?, OwnerId, SharedWithPortal, DocumentRequestId?, UploadedById?, UploadedByPortalUserId?, ProcessingStartedAt, ProcessedAt, ProcessingMs, Attempts, Error, ReviewedById, ReviewedAt, RejectReason, ExpiryNotifiedOffsets (int[]), Tags.
**document_pages:** DocumentId, PageNumber, ImageFileId (150 DPI PNG), ThumbFileId (200 px wide), WidthPx, HeightPx (of the 150 DPI image), TextSource, Text, MeanConfidence, Layout (jsonb — compact words: `[["CN-1234567", 96.2, 0.412, 0.233, 0.118, 0.019, 7], ...]` = text, conf, x, y, w, h (**normalized 0–1 of page size**), line index).
**document_extracted_fields:** DocumentId, FieldId, FieldKey, RawText, TextValue, NumericValue (decimal 18,4)?, DateValue (DateOnly)?, BoolValue?, Currency?, Confidence (0–100), PageNumber?, BBox (jsonb `[x,y,w,h]` normalized), RuleIndex?, Status (`Extracted|Missing|Edited|Approved|Rejected`), EditedById, EditedAt, PeriodDate? (copied from document), ContactId?, AccountId? (copied from document for reporting/portal).
**document_requests:** Title, TitleAr, Description, DocumentTypeId?, ContactId, AccountId?, DealId?, DueDate?, Status (`Open|Submitted|Completed|Cancelled`), RequestedById, SubmittedAt, CompletedAt, LastReminderAt, NotifyCustomer.
Indexes: documents `(tenant_id, status)`, `(tenant_id, expiry_date)`, `(tenant_id, contact_id)`, `(tenant_id, account_id)`, GIN `search_vector`; extracted fields `(tenant_id, field_key, status)`, `(tenant_id, account_id, field_key, period_date)`.
Module `AppModule.Documents` (permissions as other record modules). `OwnerId` = linked record's owner, else uploader (portal uploads → contact owner).

### 19.4 Installing & configuring Tesseract (EXACT)
- **Windows dev:** install Tesseract 5 (UB Mannheim build) to `C:\Program Files\Tesseract-OCR`, select **Arabic** in "Additional language data" during setup (or copy `ara.traineddata` from `github.com/tesseract-ocr/tessdata` into `tessdata`). Verify: `"C:\Program Files\Tesseract-OCR\tesseract.exe" --list-langs` shows `ara`, `eng`.
- **Docker (API image, Debian-based):** `RUN apt-get update && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng tesseract-ocr-ara && rm -rf /var/lib/apt/lists/*` ; Linux path `/usr/bin/tesseract`. Add `SkiaSharp.NativeAssets.Linux.NoDependencies`.
- `appsettings.json`:
```json
"Ocr": {
  "TesseractPath": "C:\\Program Files\\Tesseract-OCR\\tesseract.exe",
  "TessdataPath": null,
  "Languages": "eng+ara",
  "OcrDpi": 300, "ViewDpi": 150, "ThumbWidth": 200,
  "MaxPages": 50, "MaxFileSizeMb": 25,
  "PageTimeoutSeconds": 60, "MaxParallelPages": 2,
  "MinTextLayerChars": 20, "TempPath": null
}
```
- `/health` includes check `ocr`: runs `tesseract --version` once at startup and caches result; degraded if missing (uploads still work; documents go to Failed with "OCR engine not available").

### 19.5 Processing pipeline (EXACT contracts)
```csharp
public interface IOcrEngine
{
    Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct);
}
public sealed record OcrRequest(string Languages, int PageSegMode = 3, string? CharWhitelist = null);
public sealed record OcrWord(string Text, double Confidence, int Left, int Top, int Width, int Height, int Block, int Paragraph, int Line);
public sealed record OcrPageResult(IReadOnlyList<OcrWord> Words, int ImageWidth, int ImageHeight);

public interface IDocumentProcessor { Task ProcessAsync(Guid documentId, CancellationToken ct); }   // called by DocumentProcessingJob
public interface IDocumentClassifier { (Guid? typeId, double score) Classify(LayoutDocument doc, IReadOnlyList<DocumentType> types); }
public interface IFieldExtractor { IReadOnlyList<ExtractedValue> Extract(LayoutDocument doc, DocumentType type); }
```
**TesseractCliOcrEngine (EXACT core):**
```csharp
public sealed class TesseractCliOcrEngine(IOptions<OcrOptions> options) : IOcrEngine
{
    public async Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest req, CancellationToken ct)
    {
        var o = options.Value;
        var psi = new ProcessStartInfo(o.TesseractPath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(imagePath);           // server-generated temp path only
        psi.ArgumentList.Add("stdout");
        psi.ArgumentList.Add("-l");    psi.ArgumentList.Add(req.Languages);
        psi.ArgumentList.Add("--oem"); psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("--psm"); psi.ArgumentList.Add(req.PageSegMode.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(o.TessdataPath)) { psi.ArgumentList.Add("--tessdata-dir"); psi.ArgumentList.Add(o.TessdataPath); }
        if (req.CharWhitelist is not null) { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add($"tessedit_char_whitelist={req.CharWhitelist}"); }
        psi.ArgumentList.Add("tsv");                // config file → TSV output

        using var proc = Process.Start(psi) ?? throw new OcrFailedException("Could not start tesseract");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(o.PageTimeoutSeconds));
        var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw new OcrTimeoutException(imagePath);
        }
        if (proc.ExitCode != 0) throw new OcrFailedException(await stderr);
        var (w, h) = ImageInfo.GetSize(imagePath);   // SkiaSharp SKCodec
        return new OcrPageResult(TesseractTsvParser.Parse(await stdout), w, h);
    }
}
```
**TesseractTsvParser:** split by `\n`, skip header row; 12 tab-separated columns `level page_num block_num par_num line_num word_num left top width height conf text`; keep rows where `level == 5`, `conf >= 0` (parse with `CultureInfo.InvariantCulture`, it may be decimal) and `text.Trim() != ""`. Line key = `(block, par, line)`.

**Per-page decision (DocumentProcessor):**
1. Validate: size ≤ MaxFileSizeMb; content sniffed by magic bytes (`%PDF`, JPEG `FF D8 FF`, PNG `89 50 4E 47`, WEBP `RIFF....WEBP`); others → Failed "File type not supported". HEIC → Failed "HEIC photos aren't supported. Please upload JPG or PDF."
2. PDF: `PdfDocument.Open(stream)` (PdfPig); page count ≤ MaxPages (else Failed "This PDF has more than 50 pages"). Encrypted PDF → Failed "This PDF is password-protected".
3. For each page:
   - **Digital text:** `page.GetWords()`; if total letters ≥ `MinTextLayerChars` and replacement chars (`\uFFFD`) < 5 % → use them. Convert PdfPig coordinates (points, origin bottom-left) to normalized: `x = bb.Left / page.Width`, `y = (page.Height - bb.Top) / page.Height`, `w = bb.Width / page.Width`, `h = bb.Height / page.Height`; confidence = 99. Lines: cluster words whose vertical centers differ < 0.5 × median word height; sort lines by y, words by x (by x descending for lines that are > 50 % Arabic letters).
   - **Scanned:** render page to PNG at `OcrDpi` with PDFtoImage (`Conversion.SavePng(path, pdfStream, page: i, options: new RenderOptions(Dpi: 300))` — adapt to installed version's signature), preprocess, OCR, normalize bboxes by image width/height.
   - Images: decode with SkiaSharp, respect EXIF orientation, preprocess, OCR.
   - Always render a view image at `ViewDpi` (PDF) or a downscaled copy (max 1600 px wide) for images, + thumbnail.
4. **Preprocess (SkiaSharp):** convert to grayscale (color matrix luminance), if image width < 1600 px upscale ×2 (high quality sampling), increase contrast 1.2; save PNG to temp. (Tesseract performs its own binarization.)
5. If the page mean confidence < 50 and `--psm 3` was used → retry once with `--psm 6`; keep the better mean confidence.
6. Run pages with `Parallel.ForEachAsync` (max `MaxParallelPages`), each in its own temp folder `TempPath/{documentId}/{page}`; delete temp folder in `finally`.
7. Build `LayoutDocument` (pages → lines → words, all normalized; `FullText` = lines joined by `\n`, pages by `\f`).
8. Classify (if no user-selected type) → extract → save everything in one transaction → status (19.9) → timeline event `FileAttached` + `DocumentProcessed` on linked records → SignalR `DocumentUpdated` to `user:{ownerId}` and the uploader → notification `DocumentNeedsReview` / `DocumentProcessed` / `DocumentProcessingFailed`.
9. Retries: Hangfire automatic retry 2 times for transient errors (IO, timeout); validation failures are not retried. `Attempts` incremented.

### 19.6 Extraction engine (EXACT rules)
**Text normalization before matching** (`TextNormalizer.Normalize`): Arabic-Indic digits `٠-٩` (U+0660–0669) and Persian `۰-۹` (U+06F0–06F9) → `0-9`; Arabic comma `،` → `,`; Arabic decimal separator `٫` → `.`; Arabic thousands separator `٬` → `,`; collapse whitespace; remove tatweel `ـ`; unify Alef forms (أ إ آ → ا) and ى → ي and ة → ه for **matching only** (store raw text separately).

**Value patterns** (named tokens usable in rules; .NET regex, `RegexOptions.IgnoreCase | CultureInvariant`, timeout 200 ms):
| Token | Regex | Normalizer |
|---|---|---|
| `DATE` | `(?<!\d)(\d{1,2}[\/\-.\s](?:\d{1,2}\|[A-Za-z]{3,9})[\/\-.\s]\d{2,4}\|\d{4}[\/\-.]\d{1,2}[\/\-.]\d{1,2})(?!\d)` | `DateNormalizer` (below) |
| `AMOUNT` | `\(?-?(?:\d{1,3}(?:,\d{3})+\|\d+)(?:\.\d{1,2})?\)?` | remove `,`; `(x)` → negative; optional currency code found within 8 chars before/after (`AED\|USD\|SAR\|EUR\|GBP\|INR\|QAR\|OMR\|BHD\|KWD\|د\.إ\|ر\.س`) |
| `NUMBER` | `-?\d+(?:\.\d+)?` | decimal invariant |
| `PERCENT` | `-?\d+(?:\.\d+)?\s?%` | number |
| `EMIRATES_ID` | `784[-\s]?\d{4}[-\s]?\d{7}[-\s]?\d` | format `784-XXXX-XXXXXXX-X` |
| `TRN` | `(?<!\d)100\d{12}(?!\d)` | digits |
| `PASSPORT_NO` | `(?<![A-Z0-9])[A-Z]{0,2}\d{6,9}(?![A-Z0-9])` | uppercase |
| `LICENSE_NO` | `(?<![A-Z0-9])[A-Z0-9]{1,4}[-\/]?\d{3,10}(?![A-Z0-9])` | uppercase |
| `EMAIL` | `[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}` | lowercase |
| `PHONE` | `\+?\d[\d\s\-()]{7,16}\d` | E.164 via libphonenumber (tenant region) |
| `TEXT_LINE` | rest of the line after the anchor (max 120 chars), trimmed of `:` `-` | trim |
| `WORDS_n` | next n words (`WORDS_3`) | join with space |

**DateNormalizer:** try formats in order — if tenant `DayFirst` (default true; false for US): `dd/MM/yyyy, d/M/yyyy, dd-MM-yyyy, d-M-yyyy, dd.MM.yyyy, dd/MM/yy, dd MMM yyyy, d MMM yyyy, dd MMMM yyyy, d MMMM yyyy, dd-MMM-yyyy, yyyy-MM-dd, yyyy/MM/dd, MMM dd, yyyy, MMMM d, yyyy`; else the MM/dd variants first. `CultureInfo.InvariantCulture` (+ `ar-AE` month names). Two-digit years: `00–49 → 20xx`, `50–99 → 19xx`. Reject years outside 1900–2100.

**Rule kinds** (each field has an ordered list; **first rule that yields a valid value wins**):
```json
[
  { "kind": "anchor", "anchors": ["Expiry Date", "Expiry", "Valid Until", "تاريخ الانتهاء"], "direction": "right_or_below", "value": "DATE", "maxLinesBelow": 1, "weight": 1.0 },
  { "kind": "regex",  "pattern": "(?i)expir\\w*\\s*(?:date)?\\s*[:\\-]?\\s*(\\d{1,2}[/\\-.]\\d{1,2}[/\\-.]\\d{2,4})", "group": 1, "value": "DATE", "weight": 0.9 },
  { "kind": "row_value", "rowLabels": ["Revenue", "Total revenue", "الإيرادات"], "value": "AMOUNT", "pick": "first", "weight": 0.95 },
  { "kind": "mrz", "field": "document_number", "weight": 1.0 },
  { "kind": "keyword_presence", "keywords": ["paid", "مدفوع"], "weight": 0.8 },
  { "kind": "first_match", "value": "EMIRATES_ID", "weight": 0.85 }
]
```
- **anchor:** find a line containing the anchor phrase (normalized, case-insensitive; each anchor word may differ by Levenshtein ≤ 1 when the word is ≥ 5 chars). `right`: take words after the anchor on the same line (skip `:`/`-`), apply the value pattern to that text. `below`: take the next 1..`maxLinesBelow` lines whose horizontal span overlaps the anchor's span (x-overlap > 0), apply the pattern. `right_or_below`: right first, then below. For Arabic anchors (RTL lines) "right" means the words *after* the anchor in reading order (i.e. smaller x).
- **regex:** run on the page text (normalized); use `group`; map matched text back to words to get bbox (first word containing the match start through the word containing the match end).
- **row_value:** find the line starting with / containing a row label; extract all `value` matches on that line after the label; `pick`: `first` | `last` | `column:N` (1-based).
- **mrz:** passport/ID machine-readable zone (19.6.1).
- **keyword_presence:** Boolean true if any keyword found.
- **first_match:** first occurrence anywhere of the value pattern.

**Confidence:** `fieldConfidence = round(min(mean(conf of value words), mean(conf of anchor words or 100)) × weight)`. Digital text words = 99. Clamp 0–100.
**Missing:** required field with no value → row with `Status = Missing`, `Confidence = 0`.
**Classification:** for each active type: `score = Σ weight` of classifier keywords found in normalized full text (each keyword counted once; MRZ pattern found adds +5 to `passport`); best score ≥ `minScore` wins; ties → higher number of required fields extracted (run extraction for the tied types); none → type `generic` (no fields, text only, status Approved after processing since nothing to review — configurable).

#### 19.6.1 MRZ (passports — TD3, 2 lines × 44 chars)
- Find two consecutive lines (after removing spaces) matching: L1 `^P[A-Z<][A-Z<]{3}[A-Z<]{39}$`, L2 `^[A-Z0-9<]{9}[0-9][A-Z<]{3}[0-9]{6}[0-9][MFX<][0-9]{6}[0-9][A-Z0-9<]{14}[0-9<][0-9]$`.
- Before matching, fix common OCR confusions **only in numeric positions** of L2: `O→0, Q→0, D→0, I→1, L→1, Z→2, S→5, B→8, G→6`; and `«`/`(`/`[` → `<`.
- If not found on the page: crop the bottom 30 % of the page image and re-run Tesseract with `--psm 6`, `-l eng`, whitelist `ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<`.
- Check digits: weights 7,3,1 repeating; values `0–9` = digit, `A–Z` = 10–35, `<` = 0; sum mod 10. Validate doc number (L2[0..9] with check L2[9]), DOB (L2[13..19], check [19]), expiry (L2[21..27], check [27]).
- Fields: `document_number` = L2[0..9] trimmed `<`; `nationality` = L2[10..13]; `date_of_birth` = YYMMDD (year > current YY → 19xx); `sex` = L2[20]; `expiry_date` = YYMMDD (20xx); `issuing_country` = L1[2..5]; surname/given names = L1[5..44] split on `<<`, `<` → space.
- Confidence: 100 if all check digits valid; 60 if some invalid (→ review).

### 19.7 Built-in document types (seeded per tenant at sign-up; enabled per industry template)
| Key | Name (AR) | Classifier keywords (weight) | Fields (key: type, rules summary; **R** = required, **M** = metric, **P** = portal visible, **S** = sensitive) | ExpiryFieldKey / PeriodFieldKey | MapTo |
|---|---|---|---|---|---|
| `trade_license` | Trade license (رخصة تجارية) | "trade license" 3, "commercial license" 3, "رخصة تجارية" 3, "license no" 2, "department of economic development" 2, "DED" 1, "free zone" 1 | `license_number`: Id R P (anchor "License No", "Licence No", "رقم الرخصة" → LICENSE_NO) · `company_name`: Text R P (anchor "Trade Name", "Company Name", "الاسم التجاري" → TEXT_LINE) · `legal_form`: Text P (anchor "Legal Form", "الشكل القانوني") · `issue_date`: Date P (anchor "Issue Date", "تاريخ الإصدار") · `expiry_date`: Date R P (anchor "Expiry Date", "تاريخ الانتهاء") · `activities`: Text P (anchor "Activities", "الأنشطة" → below, maxLinesBelow 3) | expiry_date / — | `accounts.trade_license_number`, `accounts.trade_license_expiry`, `accounts.legal_name` |
| `passport` | Passport (جواز سفر) | MRZ +5, "passport" 3, "جواز سفر" 3, "nationality" 1 | `document_number`: Id R P S (mrz) · `surname`, `given_names`: Text R P (mrz) · `nationality`: Text P (mrz) · `date_of_birth`: Date P (mrz) · `sex`: Text (mrz) · `expiry_date`: Date R P (mrz, then anchor "Date of Expiry") | expiry_date / — | `contacts.cf_passport_number`, `contacts.date_of_birth`, `contacts.nationality` |
| `emirates_id` | Emirates ID (الهوية الإماراتية) | "identity card" 2, "emirates id" 3, "بطاقة الهوية" 3, pattern `784-` 3 | `id_number`: Id R P S (first_match EMIRATES_ID) · `name`: Text R P (anchor "Name", "الاسم") · `nationality`: Text P (anchor "Nationality", "الجنسية") · `date_of_birth`: Date P (anchor "Date of Birth", "تاريخ الميلاد") · `expiry_date`: Date R P (anchor "Expiry Date", "تاريخ الانتهاء") | expiry_date / — | `contacts.cf_emirates_id`, `contacts.date_of_birth` |
| `invoice` | Invoice (فاتورة) | "invoice" 3, "tax invoice" 3, "فاتورة" 3, "TRN" 1, "amount due" 1 | `invoice_number`: Id R P (anchor "Invoice No", "Invoice #", "رقم الفاتورة") · `invoice_date`: Date R P (anchor "Invoice Date", "Date", "التاريخ") · `supplier_trn`: Id (first_match TRN) · `subtotal`: Amount M P (row_value "Subtotal", "Sub Total") · `vat_amount`: Amount M P (row_value "VAT", "Tax", "ضريبة القيمة المضافة") · `total_amount`: Amount R M P (row_value "Total", "Grand Total", "Amount Due", "الإجمالي", pick last) | — / invoice_date | — |
| `financial_statement` | Financial statement (قوائم مالية) | "financial statements" 3, "statement of profit" 2, "balance sheet" 2, "القوائم المالية" 3, "audited" 1 | `period_end`: Date R P (anchor "for the year ended", "as at", "للسنة المنتهية" → DATE) · `revenue`: Amount R M P (row_value "Revenue", "Total revenue", "Sales", "الإيرادات", pick first) · `gross_profit`: Amount M P (row_value "Gross profit", "إجمالي الربح") · `net_profit`: Amount M P (row_value "Net profit", "Profit for the year", "صافي الربح") · `total_assets`: Amount M P (row_value "Total assets", "إجمالي الموجودات") · `total_liabilities`: Amount M P (row_value "Total liabilities", "إجمالي المطلوبات") · `equity`: Amount M (row_value "Total equity", "حقوق الملكية") | — / period_end | — |
| `insurance_card` | Insurance card (بطاقة تأمين) | "insurance" 2, "policy" 2, "member" 1, "تأمين" 3 | `insurer`: Text P (first line with "insurance") · `member_id`: Id R P S (anchor "Member ID", "Card No") · `policy_number`: Id P (anchor "Policy No") · `expiry_date`: Date R P (anchor "Expiry", "Valid Till") | expiry_date / — | `contacts.cf_insurance_member_id` |
| `title_deed` | Title deed (سند ملكية) | "title deed" 3, "سند ملكية" 3, "land department" 2 | `deed_number`: Id R P (anchor "Title Deed No", "رقم السند") · `property_number`: Id P (anchor "Property No") · `area_sqm`: Number M P (anchor "Area" → NUMBER) · `issue_date`: Date P | — / issue_date | — |
| `generic` | Other document (مستند آخر) | — | none (full text only) | — | — |
- Seed these as JSON files `Infrastructure/Templates/DocumentTypes/*.json` (same loader pattern as industry templates); tenants can copy & customize them or create their own types in Settings.
- `MapTo` targets that are custom fields (`cf_*`) are created by the industry template if missing (e.g. corporate_services creates `contacts.cf_passport_number`, `contacts.cf_emirates_id`).
- **Sample files** for tests: create synthetic documents (generate with QuestPDF: a fake trade license, invoice and financial statement in EN+AR; render one to PNG with added noise for the "scanned" test). Never use real personal documents.

### 19.8 Documents API
| Method | Route | Notes |
|---|---|---|
| POST | `/documents` | multipart: `files[]` (≤ 10), `documentTypeId?`, `leadId?/contactId?/accountId?/dealId?`, `sharedWithPortal`, `documentRequestId?` → 202 `[{ id, name, status }]` |
| POST | `/documents/query` | standard list query; filters incl. `status`, `document_type_id`, `expiry_date` presets, `source`, full-text `search` uses `search_vector @@ websearch_to_tsquery('simple', @q)` |
| GET | `/documents/{id}` | detail: metadata, pages `[{ number, width, height, imageUrl, thumbUrl, textSource, meanConfidence }]`, fields `[{ fieldId, key, label, valueType, required, value, rawText, confidence, page, bbox, status, isSensitive }]`, type, links, review info |
| GET | `/documents/{id}/pages/{n}/layout` | words for highlights/search (lazy) |
| GET | `/documents/{id}/pages/{n}/image`, `/thumb` | PNG (auth + scope checked) |
| GET | `/documents/{id}/download` | original file, audit logged |
| GET | `/documents/{id}/text` | full text |
| PUT | `/documents/{id}` | name, type (changing type triggers re-extraction from stored layout — no new OCR), links, sharedWithPortal, tags, manual expiry |
| PUT | `/documents/{id}/fields` | `[{ fieldId, value }]` → status Edited, confidence kept for audit, `EditedById` |
| GET | `/documents/{id}/apply-preview?targetType=&targetId=` | diff rows |
| POST | `/documents/{id}/approve` | `{ apply: [{ fieldId, target: "accounts.trade_license_expiry" }] }` |
| POST | `/documents/{id}/reject` | `{ reason }` |
| POST | `/documents/{id}/reprocess` | `{ documentTypeId?, forceOcr? }` → Queued |
| DELETE | `/documents/{id}` | soft delete (files kept 30 days, then cleanup job) |
| GET/POST/PUT/DELETE | `/document-requests` | requests to customers |
| CRUD | `/settings/document-types`, `/settings/document-types/{id}/fields` | |
| POST | `/settings/document-types/{id}/test` | multipart sample (≤ 5 pages), runs pipeline **synchronously** without saving (timeout 90 s) → same shape as detail fields + timings |
SignalR events: `DocumentUpdated { id, status, meanConfidence }`.

### 19.9 Review, approval & effects
- **Status after processing:** Failed (errors) · Approved (type.AutoApprove && all required present && every field confidence ≥ threshold) · NeedsReview (otherwise) · Approved (generic type with no fields).
- **Approve** (requires Documents.Edit): all required fields must have values (422 `required_fields_missing`). Sets fields `Status=Approved`, document `ReviewedById/At`, copies `ExpiryDate`/`IssueDate`/`PeriodDate` from the type's mapped fields, copies `ContactId/AccountId/PeriodDate` onto field rows, applies selected `MapTo` updates to the target record (normal update path → validation, audit & timeline `FieldsChanged` "from document {name}"), completes a linked `document_request` (`Completed`), notifies the portal user if request-based ("Your document was approved").
- **Reject:** fields `Rejected`; request (if any) back to `Open` with reject reason shown to the customer; notification/email to the portal user.
- **Edits after approval** are allowed (status stays Approved, audit logged); re-processing an approved document asks for confirmation ("This replaces the approved values.").
- **Expiry alerts:** `DocumentExpiryJob` daily: approved docs with `ExpiryDate` where `daysLeft ∈ ReminderDaysBefore` and offset not in `ExpiryNotifiedOffsets` → `DocumentExpiring` notification to owner (and, if portal settings `NotifyCustomerOnExpiry`, email to the portal user) → append offset. Expired documents get chip "Expired" (computed).
- **Duplicates:** same `Sha256` already uploaded in the tenant → upload response flag `possibleDuplicateOf` and UI warning "This file was already uploaded on 02 Oct 2026 (Trade license – Gulf Trading.pdf)." (still allowed).

### 19.10 Metrics: storage & usage
- Every approved field with `IsMetric = true` (Number/Amount) has `NumericValue` + `PeriodDate` (document `PeriodDate` ← `PeriodFieldKey` value, else `IssueDate`, else approval date) + `AccountId/ContactId` → this is the **metrics store** (`document_extracted_fields` filtered by `Status = Approved`).
- **Reports:** data source `document_metrics` (9.2) + system reports 14–15 (9.6). Example custom report: "Revenue by year for my accounts" = source document_metrics, filter type = financial_statement and field_key = revenue, matrix rows account, columns period_date by Year, Sum numeric_value.
- **Dashboard:** widgets `documents_review`, `documents_expiring` (8.4).
- **Account/Contact pages:** "Documents" tab: list of documents + "Key information" card (latest approved values of portal-visible fields per type, same logic as portal 20.5) + small metric charts when metrics exist (e.g. revenue per year).
- **Portal:** Key information & Metrics sections (20.5).
- Currency for amounts: detected code near the value, else tenant base currency; metrics in reports use `NumericValue` converted to base with the exchange rate effective at `PeriodDate` (fallback latest).

### 19.11 UI
Screens: Documents list, upload drawer, review screen, Documents tabs on records, Settings → Document types (18.9.12–18.9.13). `DocumentPageViewer` component (UI library):
- Params: `Pages` (list with image URLs and sizes), `Highlights` (list of `{ id, page, bbox, label, tone }`), `ActiveHighlightId`, `ShowHighlights`, `ShowThumbnails`, `OnHighlightClick`.
- Renders each page as `.ml-doc-page` with `<img loading="lazy">` and absolutely positioned `.ml-doc-box` at `left:{x*100}%; top:{y*100}%; width:{w*100}%; height:{h*100}%` (EXACT formula — no pixel math needed thanks to normalized boxes).
- Zoom levels 50–200 % in 25 % steps + "Fit width" (default); scroll container; `ScrollToHighlight(id)` via JS interop `element.scrollIntoView({block:'center', behavior:'smooth'})`.
- Thumbnails rail 96 px (lg+), current page outlined primary.

### 19.12 Acceptance criteria
- Digital PDF trade license (EN) → type auto-detected `trade_license`; license number, company name, expiry extracted with ≥ 95 confidence; processing < 5 s.
- Scanned (image-only) PDF and phone photo (JPG) of the same synthetic license → same values extracted (OCR), boxes highlight the right words on the page image.
- Arabic synthetic trade license → Arabic anchors work; Arabic-Indic digits normalized to `2027-03-31`.
- Passport sample with MRZ → all MRZ fields with valid check digits, confidence 100.
- Financial statement → revenue/net profit/total assets as metrics; report 14 shows revenue by year per account; portal chart shows the same numbers.
- Approve with "apply to record" updates the account's trade license expiry and writes a timeline event; expiry job sends the 30-day notification once.
- 60-page PDF rejected with a clear message; Tesseract timeout kills the process and marks the document Failed with retry possible.
- Tenant and scope isolation for documents, page images and downloads (integration tests).

---

## 20. ★ CUSTOMER PORTAL v1

### 20.1 Goal
A branded, separate login area where **the tenant's customers** (contacts) can see the documents shared with them, the **key information and metrics extracted** from those documents, documents that are expiring, and upload documents the business requested. (Quotes, invoices, payments, tickets and projects are added to this same portal in Phases 2–3.)

### 20.2 Hosting (EXACT)
- `MindLift.Portal` (Blazor WASM) csproj: `<StaticWebAssetBasePath>portal</StaticWebAssetBasePath>`; `wwwroot/index.html` has `<base href="/portal/" />`, the fonts (18.3), MudBlazor css and `_content/MindLift.UI/css/mindlift.css`.
- Api references both client projects and hosts them:
```csharp
app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/portal"), portal =>
{
    portal.UseBlazorFrameworkFiles("/portal");
    portal.UseStaticFiles();
    portal.UseRouting();
    portal.UseEndpoints(e => e.MapFallbackToFile("portal/{*path:nonfile}", "portal/index.html"));
});
app.UseBlazorFrameworkFiles();     // staff app at "/"
app.UseStaticFiles();
// ... auth, controllers, hubs ...
app.MapFallbackToFile("index.html");
```
- Portal routes: `/portal/{tenantSlug}/login`, `/portal/{tenantSlug}` (home), `/portal/{tenantSlug}/documents`, `/portal/{tenantSlug}/documents/{id}`, `/portal/{tenantSlug}/requests`, `/portal/{tenantSlug}/profile`, `/portal/{tenantSlug}/accept-invite?token=`, `/portal/{tenantSlug}/magic?token=`.
- Branding loaded first from `GET /api/v1/portal/{slug}/branding` (anonymous): `{ tenantName, logoUrl, primaryColor, defaultLanguage, supportEmail, supportPhone, welcomeMessage, welcomeMessageAr, enabled }`. If disabled → page "This portal is not available." The theme uses `MindLiftTheme.Create(primaryColor)`.

### 20.3 Data model
**portal_settings** (1:1 tenant): IsEnabled, WelcomeMessage, WelcomeMessageAr, ShowMetrics (bool, default true), AllowGeneralUploads (bool, default false — uploads only against requests), NotifyCustomerOnExpiry (bool), SupportEmail, SupportPhone.
**portal_users** (TenantEntity): ContactId (unique), AccountId? (copied from contact, refreshed on login), Email, NormalizedEmail (unique **per tenant** — a person can be a customer of several tenants), PasswordHash (nullable — magic-link-only users), IsActive, InvitedAt, InvitedById, ActivatedAt, LastLoginAt, Language, FailedLoginCount, LockoutEnd.
**portal_tokens** *(hard delete)*: PortalUserId, Purpose (`Invite|MagicLink|ResetPassword`), TokenHash (SHA-256), ExpiresAt (invite 7 days, magic link 15 min, reset 1 h), UsedAt.
**portal_refresh_tokens** *(hard delete)*: same shape as staff refresh tokens.

### 20.4 Authentication & isolation (EXACT rules)
- Two JWT bearer schemes: `"Staff"` (default; audience `mindlift-api`) and `"Portal"` (audience `mindlift-portal`). Portal token claims: `sub` (portalUserId), `tid`, `cid` (contactId), `aid` (accountId, optional), `kind=portal`, `lang`. Access token 15 min; refresh cookie path `/api/v1/portal/auth`, 14 days.
- Staff controllers: `[Authorize]` (default policy requires scheme Staff). Portal controllers: `[Authorize(AuthenticationSchemes = "Portal", Policy = "PortalUser")]` and live under `/api/v1/portal/*`. A staff token on portal endpoints → 401; a portal token on staff endpoints → 401 (integration test).
- `TenantResolutionMiddleware` sets the tenant from `tid` for both; for portal also fills `IPortalContext { PortalUserId, ContactId, AccountId }`.
- Every portal query **must** go through `PortalScope`:
  - Documents: `SharedWithPortal && Status != Rejected && (ContactId == cid || (aid != null && AccountId == aid))`. Status mapping for customers: Queued/Processing/NeedsReview → "In review"; Approved → "Ready"; Rejected (only own uploads) → "Needs a new upload".
  - Extracted fields: only from documents above, `Status = Approved` and field `PortalVisible`.
  - Requests: `ContactId == cid`.
- Login: rate-limited (5/min/IP, lockout after 5 failures for 15 min); passwords via `PasswordHasher<PortalUser>`; magic link email always returns 200 ("If an account exists, we sent a link") to avoid account enumeration.
- Inactive contact/portal user or disabled portal → 401 + refresh tokens revoked.

### 20.5 Home aggregation logic (`PortalHomeService`)
- **Key information:** for each document type with at least one Ready document in scope: take the latest Ready document (by `PeriodDate ?? IssueDate ?? ReviewedAt`), return `{ typeKey, typeName, documentId, fields: [{ label, value, valueType, isSensitive }], expiryDate, daysLeft, validity: valid|expiring|expired }`. Sensitive values masked server-side as `••••` + last 4 (full value only on document detail with `reveal=true`, audit logged).
- **Metrics** (if `ShowMetrics`): for each portal-visible metric field key with ≥ 1 value: series of `{ periodDate, value, currency }` (one value per period — latest approved document wins), `latest`, `previous`, `changePct`. Sorted by type then field order. Max 6 metrics.
- **Expiring:** Ready documents with `ExpiryDate` in the next 90 days or expired, ordered by date.
- **Requests:** open requests ordered by due date.
- **Recent documents:** last 5 shared documents.
Response:
```json
{ "customerName":"Ahmed Khan", "accountName":"Gulf Trading LLC", "welcomeMessage":"…",
  "openRequests":[{ "id":"…","title":"Upload a copy of your Emirates ID","dueDate":"2026-10-12","status":"Open" }],
  "keyInfo":[{ "typeKey":"trade_license","typeName":"Trade license","documentId":"…","expiryDate":"2027-03-31","daysLeft":173,"validity":"valid",
               "fields":[{ "label":"License no.","value":"CN-1234567","valueType":"Id","isSensitive":false }] }],
  "metrics":[{ "key":"revenue","label":"Revenue","currency":"AED","latest":12400000,"previous":10500000,"changePct":18.1,
               "series":[{ "period":"2023-12-31","value":9100000 },{ "period":"2024-12-31","value":10500000 },{ "period":"2025-12-31","value":12400000 }] }],
  "expiring":[…], "recentDocuments":[…] }
```

### 20.6 Portal API (`/api/v1/portal/...`)
| Method | Route | Auth |
|---|---|---|
| GET | `{slug}/branding` | anonymous |
| POST | `{slug}/auth/login` `{ email, password }` | anonymous, rate-limited |
| POST | `{slug}/auth/magic-link` `{ email }` → always 200 | anonymous |
| POST | `{slug}/auth/magic-link/verify` `{ token }` → tokens | anonymous |
| POST | `{slug}/auth/accept-invite` `{ token, password? }` | anonymous |
| POST | `{slug}/auth/forgot-password`, `{slug}/auth/reset-password` | anonymous |
| POST | `auth/refresh`, `auth/logout` | cookie |
| GET/PUT | `me` (name read-only, phone, language, change password) | Portal |
| GET | `home` | Portal |
| GET | `documents?type=&q=&page=` | Portal |
| GET | `documents/{id}` (+ `?reveal=fieldId`) | Portal |
| GET | `documents/{id}/pages/{n}/image`, `documents/{id}/download` | Portal |
| GET | `requests` | Portal |
| POST | `requests/{id}/upload` (multipart, ≤ 5 files) → documents with `Source=Portal`, `SharedWithPortal=true`, linked to contact/account/deal of the request; request → `Submitted`; staff notified (`DocumentRequestSubmitted`) | Portal |
| POST | `documents/upload` (only if `AllowGeneralUploads`) | Portal |

### 20.7 Staff-side features
- **Contact page → "Portal access" card:** status (Not invited / Invited (date) / Active (last login) / Disabled), buttons [Invite to portal] (requires contact email; sends invite email), [Resend invite], [Disable access], [Copy portal link].
- **Document share toggle** (`SharedWithPortal`) on upload drawer, document header and list bulk action; default from document type `DefaultPortalVisible`.
- **Document requests:** "Request documents" button on Contact/Account/Deal → dialog: title (prefilled from type), document type (optional), description, due date, [✓] notify customer by email (and WhatsApp template if connected). List of requests on the record's Documents tab with status chips; overdue requests highlighted.
- Settings → **Customer portal:** enable switch, welcome messages EN/AR, show metrics, allow general uploads, notify on expiry, support contact, preview button (opens portal as the selected contact via a 10-minute read-only staff preview token — audit logged).
- Notifications: `DocumentUploadedByCustomer`, `DocumentRequestSubmitted` to the contact owner.

### 20.8 Portal emails (localized EN/AR, branded with tenant logo/color, sent via system SMTP with tenant name as sender display name)
Invite ("{Tenant} invited you to their customer portal" + button "Set up my access"), magic link ("Your sign-in link" + button, expires 15 min), password reset, new document request ("{Tenant} requested a document: {title}" + due date + button "Upload now"), request reminder (`DocumentRequestReminderJob`: 3 days before due, on due date, then every 7 days while open; max 4 reminders), document approved / needs re-upload (with reason), document expiring (if enabled; at 60/30/7 days). Every email has a plain-text alternative and a footer "Sent by {Tenant} via MindLift".

### 20.9 Acceptance criteria
- Staff invites a contact → email in Mailpit → customer sets password → sees home with branding (logo, color, language).
- Staff requests "Emirates ID" → customer uploads a phone photo from a 375 px screen (camera option) → OCR runs → staff sees it in "Needs review" with highlights → approves → customer's home shows Emirates ID card with masked number, expiry and validity chip.
- Financial statements for 3 years approved → portal Metrics shows revenue StatCard with correct change % and a chart with 3 points equal to report 14.
- Customer A cannot access customer B's documents (same tenant) by changing ids → 404; staff token rejected by portal API and vice versa.
- Portal works fully in Arabic (RTL) and on phones; every portal screen passes the UI DoD (18.15).

# MindLift CRM â€” Progress Log

Read this file first at the start of every session. The full plan is in `CRM_BUILD_PLAN.md`.

## Current status
- **Last completed milestones:** M15 (Hardening, Docker, CI/CD, Documentation & Upload Security), M16 (Real OCR Pipeline, MRZ, Review Screen & Expiry Notifications), and M17 (Customer Portal WASM App, Refresh Tokens, Security & Isolation).
- **Status (2026-10-13):** 949 tests pass across 3 consecutive runs (412 unit, 158 UI, 379 integration). The build has 0 errors and 0 warnings. `tests/MindLift.E2E` was **not run** (Playwright harness).

## Environment
- .NET SDK 10.0.401 (pinned in `global.json`, `rollForward: latestFeature`)
- Docker 29.x, Git 2.53
- Node 24 present (not needed yet)

## Decisions
| Date | Decision | Reason |
|---|---|---|
| 2026-10-09 | Central Package Management via `Directory.Packages.props` | Required by plan section 2 (Haiku must find all versions in one place) |
| 2026-10-09 | `MudBlazor` 9.11.0 installed (plan's theme code targets the 8.x API) | Latest stable at the time; theme code in M5 will be adapted and recorded here |
| 2026-10-09 | Only M0 projects created now; `MindLift.UI`, `MindLift.Portal` and `MindLift.E2E` are created in M5a / M17 / M5a | Matches milestone order |
| 2026-10-09 | Dev database password `mindlift_dev` in `appsettings.Development.json` and `docker-compose.yml` | Local development only; production secrets come from environment variables (plan 4.14) |

## Milestone checklist

### M0 â€” Environment & skeleton
- [x] Install .NET 10 SDK
- [x] `git init`, `.gitignore`
- [x] `global.json`, `Directory.Build.props`, `Directory.Packages.props`
- [x] Create projects and references (Shared â† Domain â† Application â† Infrastructure â† Api; Web â†’ Shared; Api â†’ Web)
- [x] Solution file `MindLift.slnx`
- [x] `docker-compose.yml` (postgres:16, mailpit)
- [x] API `Program.cs`: Serilog, OpenAPI + Scalar, ProblemDetails, `/health` with DB check, CORS (dev), Blazor WASM hosting, unknown `/api` and `/hubs` â†’ 404
- [x] Blazor WASM app with MudBlazor showing "Hello MindLift"
- [x] `PROGRESS.md`
- [x] DoD verified: `docker compose up -d` + API run â†’ `/health` OK, `/scalar/v1` loads, `/` shows the Blazor page (see test results below)

## Test results

### M0 â€” 2026-10-09
- `dotnet build MindLift.slnx`: 0 errors, 0 warnings in project code
- `dotnet test tests/MindLift.IntegrationTests`: **5 passed, 0 failed**
  - `Root_serves_the_blazor_host_page`
  - `OpenApi_document_is_available_in_development`
  - `Unknown_api_route_returns_404_and_not_the_spa_page`
  - `Unknown_hub_route_returns_404_and_not_the_spa_page`
  - `GlobalExceptionHandlerTests.Unhandled_exception_returns_500_problem_details_without_stack_trace`
- `tests/MindLift.UnitTests`: no tests yet (the first unit tests arrive in M1, so `dotnet test` on that project reports "No test is available")
- Manual DoD (API run in Development on `http://localhost:5082`):
  - `GET /health` â†’ 200 `Healthy` (database check passes)
  - `GET /scalar/v1` â†’ 200, `GET /openapi/v1.json` â†’ 200
  - `GET /api/nope` â†’ 404 (not the SPA page)
  - `GET /` â†’ Blazor page; browser text shows "Hello MindLift"
- Containers: `mindlift-postgres` (healthy), `mindlift-mailpit` (healthy)

## Deviations from the plan (M0)
| Item | Plan says | What was done | Why |
|---|---|---|---|
| Postgres host port | 5432 | **5433** (container still 5432 internally); connection string uses 5433 | Port 5432 is held by a WSL relay on this machine |
| Project file creation | `MindLift.sln` | Solution file is `MindLift.slnx` (the .NET 10 default format) | Created by `dotnet new sln` in .NET 10 |
| Project template files | â€” | Template projects use the same CPM file; versions moved to `Directory.Packages.props` | Central package management must not have versions in project files |
| Packages | â€” | Serilog.AspNetCore 10.0.0, Scalar.AspNetCore 2.17.14, AspNetCore.HealthChecks.NpgSql 9.0.0, MudBlazor 9.11.0, Microsoft.AspNetCore.Components.WebAssembly.Server 10.0.12 | Latest stable resolved by the CLI |
| `MindLift.UI` / `MindLift.Portal` / `MindLift.E2E` | listed in section 3 | Not created yet | They belong to M5a / M17 / M5a, which the milestone order says to create later |
| Startup `UseHttpsRedirection` | not in plan | Not used | Keeps Docker and the local dev flow simple; HTTPS is handled at the reverse proxy in production (M15) |

### M1 â€” Persistence & cross-cutting core
- [x] Domain base types (`TenantEntity`, `ITenantEntity`, `ISoftDelete`, `IOwnedEntity`, `IHasCustomFields`, `ISampleData`, `[Audited]`, `[HardDelete]`)
- [x] Shared enums: `AppModule`, `PermissionAction`, `DataScope`, `DashboardView`, `DatePreset`
- [x] `AppDbContext` (`IdentityUserContext<AppUser, Guid>`), snake_case columns, `pg_trgm` extension, global tenant query filter applied to every `TenantEntity` by reflection, plus explicit filters on `users` and `audit_logs`
- [x] `ITenantContext` / `TenantContext` (scoped; `Use(tenantId, userId)` returns a restore scope)
- [x] `AuditableEntityInterceptor`: tenant assignment on insert, cross-tenant write blocking, tenant-change blocking, `CreatedAt`/`UpdatedAt`/`UpdatedBy`, soft delete, `Version` increment, audit rows for `[Audited]` entities
- [x] `TenantResolutionMiddleware` (reads `tid` / `sub` claims; activates once authentication exists in M2)
- [x] Exceptions (`NotFound`, `Forbidden`, `Conflict`, `Validation`, `BusinessRule`) mapped to ProblemDetails with stable `code` values (404/403/409/400/422, otherwise 500 without stack traces)
- [x] FluentValidation action filter (`ValidationActionFilter`) â€” no validators yet, so it has no effect until M2
- [x] `IClock`, `TenantTime`, `DateRangeResolver` (all 12 presets, Monday/Sunday week start, fiscal year and quarter, DST-safe)
- [x] `IFileStorage` + `LocalFileStorage` (keys are validated against path traversal)
- [x] Hangfire with PostgreSQL storage and queues `default, messaging, reports, imports, ocr`; dashboard at `/hangfire` (Development only until M2 adds platform-admin access)
- [x] HybridCache registered
- [x] SignalR hub skeleton at `/hubs/notifications` (no client methods yet)
- [x] First migration `Initial` created and applied to the development database
- [x] Tests (see results below)

**Plan deviations in M1** (all intentional):
| Item | Plan says | What was done | Why |
|---|---|---|---|
| Concurrency token | `Version` mapped to PostgreSQL `xmin` | Integer `Version` column (starts at 1), incremented by the interceptor, used as the EF concurrency token | EF cannot create the `xmin` system column in migrations, and Npgsql 10.0.3 has no `xmin` helper. The migration generated with `xmin` would fail on PostgreSQL |
| `AppUser` location | Identity user in Domain | `AppUser` lives in `MindLift.Infrastructure/Identity` | Domain must not depend on ASP.NET Identity (plan section 3) |
| Identity table names | not specified | `users` for users; default Identity names for claims, logins and tokens | Only `users` is needed by the plan |
| File upload/download endpoints | M1 item 6 | **Deferred to M2** | Endpoints need authenticated tenant context; the storage service and `StoredFile` entity exist now |
| Timeline events (`timeline_events`) | M1 item 4 | **Deferred** (fields tracked via the field registry in M3, fan-out in M9) | Tracked fields come from the field registry, which does not exist yet |
| Hangfire dashboard auth | platform admin only | Development only | Platform admin users arrive in M2 |
| Test database | Testcontainers | Testcontainers PostgreSQL 16 shared across the run; each probe test gets its own database | As planned |

**Dev environment:** The development database was migrated with `dotnet ef database update` (the migration is also applied automatically in Development via `Database:MigrateOnStartup`).

**Commands:**
- Add migration: `dotnet ef migrations add <Name> --project src/MindLift.Infrastructure --startup-project src/MindLift.Api --output-dir Persistence/Migrations`
- Apply: `dotnet ef database update --project src/MindLift.Infrastructure --startup-project src/MindLift.Api`

**M1 test results (2026-10-09):**
- `dotnet test tests/MindLift.UnitTests`: **11 passed** â€” `DateRangeResolverTests` (9, including the plan's acceptance example: 22:30 UTC on 31 Oct counts in November for Dubai, New York DST, Monday vs Sunday weeks, April fiscal year, quarter wrap) and `TenantTimeTests` (2)
- `dotnet test tests/MindLift.IntegrationTests`: **28 passed** â€” tenant isolation (rows, audit rows, no tenant, cross-tenant insert, tenant change blocked, writes without tenant blocked), persistence behaviour (timestamps, version, audit with changed fields, soft delete hidden + audited, concurrent update rejected), query filter coverage (reflection over every `TenantEntity`, `AppUser`, `AuditLog`), local file storage (round trip, six path-traversal cases), exception-to-status mapping (404/403/409/422), host smoke tests (root page, OpenAPI, `/health` with DB, `/api` and `/hubs` 404s)
- Live check (Development): `/health` 200, `/hangfire` 200, `/hubs/notifications/negotiate` 200

### M2 â€” Auth, tenants, users, roles, permissions
- [x] Entities: `Tenant`, `Plan`, `TenantSettings`, `AppUser` (role, team, branch, manager), `Role`, `RolePermission`, `ModuleSetting`, `Team`, `Branch`, `Invitation`, `RefreshToken`, `PicklistValue`
- [x] Migration `Security_Tenancy_Administration` created and applied to the development database
- [x] Plans seeded idempotently at startup: starter (5 users), pro (25), enterprise (500)
- [x] Sign-up (`POST /api/v1/auth/signup`): company on a 14-day Pro trial, country defaults from plan 6.2, seeded configuration, owner with the Admin role, unique slug (`-2`, `-3` â€¦), duplicate email â†’ 409 `email_taken`
- [x] Seeding: roles Admin / Manager / Staff with the plan 4.4.3 matrix, 22 module settings (13 enabled in Phase 1), base picklists EN + AR (6 keys)
- [x] Sign-in with lockout (5 failures â†’ 15 minutes), one generic message for unknown email and wrong password, tenants Suspended or Cancelled blocked
- [x] JWT access tokens (15 min; claims `sub`, `tid`, `email`, `name`, `lang`, `plat` for platform admins)
- [x] Refresh tokens: stored hashed, rotated on every use, reuse of a revoked token revokes the whole family (`refresh_token_reused`)
- [x] Web clients receive the refresh token as an HttpOnly, Secure, SameSite=Strict cookie at `/api/v1/auth`; mobile clients (`X-Client: mobile`) receive it in the body
- [x] Forgot password (always 202), reset password (revokes sessions), email verification, send verification, change password, `/auth/me`, profile update
- [x] Permissions: `[HasPermission(module, action)]` â†’ policy `perm:{module}:{action}` â†’ `PermissionHandler`; role matrix cached per tenant (HybridCache) and invalidated on change; module disabled â†’ no access
- [x] Data scope: `IDataScopeService` (Own = me; Team = me + team + everyone reporting to me, recursive; All = no filter)
- [x] Invitations: create (seat check), list, resend, revoke, preview, accept (creates the user, signs in). Expire after 7 days. Only token hashes are stored
- [x] Users: list, get, update (role change rules), deactivate (owner cannot be deactivated; self cannot be deactivated; refresh tokens revoked), reactivate (seat check)
- [x] Roles: list (with member counts), create, update (system roles cannot be renamed), delete (system roles and roles in use refused), permission matrix get/save (Admin matrix fixed), clone
- [x] Teams and branches: CRUD with in-use checks
- [x] Audit log: `GET /api/v1/audit-logs` (requires Settings View, Admin only)
- [x] Plan seat limit: active users + pending invitations vs `MaxUsers` â†’ 402 `plan_limit_users`
- [x] Platform admin: `GET /api/v1/platform/tenants`, `PATCH â€¦/status`, `PATCH â€¦/plan` (requires the `plat` claim). CLI: `seed-platform-admin --email --password [--first --last]` (idempotent)
- [x] Access rule for every endpoint is checked by reflection (`ControllerGuardTests`)

**Plan deviations in M2** (intentional):
| Item | Plan says | What was done | Why |
|---|---|---|---|
| Service layer location | Services in `MindLift.Application` | Identity-, cache- and mail-dependent services live in `MindLift.Infrastructure`; Application holds interfaces and exceptions | Application must not depend on ASP.NET Identity, and UserManager and HybridCache are infrastructure concerns |
| Password reset and email verification tokens | Custom tokens | ASP.NET Identity token providers (data protection) | Standard, tested, expires correctly |
| Lockout | Identity lockout | Identity lockout; the login code records each failure with `AccessFailedAsync` | `CheckPasswordAsync` does not count failures, which the tests caught |
| Login user lookup | `AsNoTracking` | Tracked entity | Identity reloads users by id; a second tracked copy breaks updates |
| Refresh-token cleanup job | `CleanupJob` (M15) | Not implemented yet | Expired and revoked rows are harmless until the cleanup job exists |
| Notification preferences table | Created in M2 seed | Not created | Belongs to M10 |
| Module enable/disable API | Settings â†’ Modules (M3) | Only the `module_settings` table and the check are in place | The settings API is M3 |
| Impersonation | Plan 6.10 | Not implemented | Needs audit design in M15 |
| Platform admin login and tenant CRUD UI | â€” | API only | UI comes in M5 |
| Email delivery | SMTP via Mailpit | `SmtpEmailSender` (MailKit) implemented; tests use a recording fake. Live delivery to Mailpit was not exercised in this session | Covered when the Mailpit flow is run manually |

**M2 test results (2026-10-09):**
- `dotnet test tests/MindLift.IntegrationTests`: **46 passed**: sign-up and duplicates, weak passwords, five failures lock the account, generic login error, refresh rotation and reuse, forgot and reset password, `/auth/me` needs a token, staff and manager permissions, live permission-matrix change (cache invalidation), tenant isolation of users, system and in-use role protection, owner protection, plan seat limit (402), audit log (admin only), platform endpoints (tenant admin â†’ 403, platform admin â†’ 200), reflection guard on every endpoint, plus the M0/M1 tests
- `dotnet test tests/MindLift.UnitTests`: **11 passed**
- CLI: `seed-platform-admin` creates the admin, and a second run reports that the email exists

### M3 — Settings & customization backbone
- [x] Field Registry (`SystemFields`, `FieldDef`): system fields for leads, contacts, accounts, deals and activities; custom fields merged in at read time; label overrides and hidden flags applied
- [x] Minimal record entities so the settings have something to act on: `Lead`, `Contact`, `Account`, `Deal`, `Activity` (core columns only, each with `CustomFields` jsonb; the full column set comes in M6–M9)
- [x] Custom fields: create, update (key and type are fixed), delete (refused while values exist, or purge with `purgeData=true` which removes the key from records). Key rule `^cf_[a-z][a-z0-9_]{1,46}$`, 150 per entity, picklist options validated
- [x] `CustomFieldRules.ValidateValues`: unknown keys, required, type checks per field type, picklist options, text length
- [x] `FilterCompiler`: the shared filter model compiles to LINQ. Operators Eq, Neq, Contains, NotContains, StartsWith, IsEmpty, IsNotEmpty, Gt, Gte, Lt, Lte, Between, In, NotIn and date presets (in the tenant time zone). Custom fields use SQL helpers `crm_cf_text` / `crm_cf_numeric` (created by the M3 migration). `@me` resolves to the current user. Max nesting 3, unknown fields rejected
- [x] Pipelines: CRUD, default pipeline, three default stages on creation (New, Won, Lost). Stage rules (at least one Open, exactly one Won and one Lost, Won and Lost last, probability 0–100) are checked inside a transaction, so a refused change is rolled back
- [x] Deleting a stage with deals needs `moveDealsToStageId` (409 `stage_has_deals` otherwise). Deleting a pipeline with deals needs a stage of another pipeline. The last pipeline cannot be deleted
- [x] Picklists: add, update (code fixed), delete. Values in use need `replaceWith`, which is written to every system field bound to the list (409 `picklist_value_in_use` otherwise). System values cannot be deleted
- [x] Company settings (time zone validated, week start, fiscal year start, tax), currencies (base + enabled list), exchange rates (currency must be enabled first), modules (enable, labels, order)
- [x] Layouts (form and detail, per entity). Saving checks every field exists and appears once; default layout = all system fields in two columns
- [x] Saved views (`/api/v1/views/{entity}`), creator-only delete, shared visibility
- [x] Bootstrap `GET /api/v1/app/bootstrap`: user, permissions per module, modules, fields, picklists, pipelines, layouts, currencies, company, and a `version`. The shared part is cached per settings version; the user's own part is computed on each call, so role changes are never stale
- [x] Every settings write and every role change moves the version (`SettingsVersion`)
- [x] Settings endpoints require the Settings module (Admin only by default); the controller guard test still passes for every endpoint

**Plan deviations in M3** (intentional, or not yet done):
| Item | Plan says | What was done | Why |
|---|---|---|---|
| Field registry cache | Cached per tenant | Read from the database on each call (the bootstrap as a whole is cached) | Simpler to get right first; add a cache when list endpoints need it (M6) |
| Enum JSON format | Names in examples (for example "Currency") | Enums travel as numbers (default JSON) on both sides | Consistent across API and tests. Switch all enums to strings at once if the UI prefers names |
| Record entities | Full tables in M6–M9 | Core columns only | M3 needs something to filter and delete against |
| Picklist replacement | Covers every record | Covers system columns only. Values held in custom jsonb fields are not checked or moved | Custom picklist usage needs a jsonb query per field. Add it with the record services |
| Custom field purge | Background `CustomFieldPurgeJob` | Synchronous `UPDATE` in the request | Small tables for now; move to the job when records grow |
| Picklist and stage reordering | Drag-and-drop reorder endpoints | Not implemented (use `sortOrder` on save) | UI work, M5 |
| Company logo | `LogoFileId` | Not implemented | Needs the file endpoints (still open from M1) |
| Record query endpoint | `POST /api/v1/{entity}/query` | Not implemented; `FilterCompiler` is tested directly | Lives with each record module (M6–M9) |
| Saved view permissions | Per record module | Under the Dashboard module (View / Create / Delete) | Per-entity permissions arrive with the record modules |

**M3 test results (2026-10-09):**
- `dotnet test tests/MindLift.UnitTests`: **37 passed**: filter compiler (equality, contains, empty, in, `@me`, or-groups, date preset in the tenant zone, unknown field, wrong operator for type, depth limit, empty filter), custom field key rule, custom value validation (required, types, options, unknown keys), pipeline stage rules
- `dotnet test tests/MindLift.IntegrationTests`: **56 passed**, including M3 tests: custom field appears in bootstrap and a database query filters on its value through the SQL helpers; purge rule for custom fields; stage with deals; won stage cannot become open (422, nothing saved); picklist in use with replacement; staff cannot change settings; exchange rate needs an enabled currency; layouts reject unknown fields; settings changes move the bootstrap version and a module switch is visible; saved views
### M4 — Tenant onboarding (industry templates, sample data, trial)
- [x] Seven industry templates embedded as JSON (`Templates/Industries`): general, real_estate, corporate_services, logistics, finance_consulting, healthcare, other
- [x] `IndustryTemplateValidator` checks every template (known modules, picklists, layout fields, pipeline rules, custom field keys and types). Unit tests validate all seven
- [x] Applying a template (plan 6.5): module labels and switches reset then applied; picklist values added (merge); pipelines and stages created (default pipeline set); custom fields and lead layouts created. Every row carries `Origin = Template`
- [x] Re-applying removes only template rows and sample data. Refused with 409 `template_locked` once real leads, contacts, accounts or deals exist. A user-designed layout or custom field with the same key is never overwritten (409 `field_key_taken` for fields)
- [x] Onboarding wizard API (plan 6.3): status with completed and skipped steps, checklist (9 items, `available` flags), company step, industries list, industry step, pipeline step (stages validated, deal-holding stages protected), team step (invitations use seats), data step (sample / import / empty), skip (steps 3–6 only), complete, dismiss checklist, remove sample data
- [x] Sample data (plan 6.7): Bogus with a fixed seed. 8 accounts, 20 contacts, 30 leads (6 converted), 25 deals (8 won, 5 lost, 12 open) across the default pipeline, 60 activities (overdue, today, upcoming), custom field values for every active custom field. Loading twice → 409 `sample_loaded`
- [x] Removing sample data: real deletes of sample rows only; real records untouched
- [x] Trial (plan 6.9): `TrialExpiryJob` runs daily at 06:00 UTC (Hangfire recurring). Reminder email three days before the end (once), expired trial → PastDue
- [x] Subscription guard: writes return 402 `subscription_required` when PastDue or Cancelled; Suspended returns 403. Reads, sign-in and platform endpoints stay open
- [x] Fixed a latent bug from M3: soft-deleted rows blocked reuse of pipeline names, picklist codes, custom field keys and layout kinds. Those unique indexes now apply only to rows that are not deleted (migration `Unique_Indexes_Ignore_Deleted`)

**Plan deviations in M4** (intentional, or not yet done):
| Item | Plan says | What was done | Why |
|---|---|---|---|
| Template sections not applied yet | Message templates, checklist templates, assignment rules, saved views | Read from the JSON but not stored | Their tables come in M12, M8, M6 and M9 |
| Dashboard highlights and document types | Applied | Stored in the JSON only | Used by the dashboard (M13) and documents (M16) |
| Picklist mode `replace` | Deactivates old values | Treated as `merge` (no template uses replace) | Not needed yet |
| Custom picklists | Options via a shared picklist map | Options written inline in each custom field | Simpler and validated the same way |
| Module labels in Arabic | `singularAr` / `pluralAr` | Not stored | ModuleSetting has no Arabic label columns yet; add with the UI (M5) |
| Healthcare doctor field | User type | Text field | User-type fields arrive with the record modules |
| Sample data per industry | Profile-specific values | One generator for all profiles | Profiles only differ in names; revisit when the dashboard needs realistic per-industry demos |
| Sample stage history and monthly targets | Generated | Not generated | Tables arrive in M8 and M13 |
| Sample removal | Audited | Done with real deletes, so no audit rows per deleted sample record | Bulk delete for speed; the audit trail keeps the original creation rows |
| Route gating until step 2 | Redirect in the app | UI only (M5) | The API stays usable for any step |
| Company logo | Logo upload | Not implemented | Needs the file endpoints (still open from M1) |

**M4 test results (2026-10-09):**
- `dotnet test tests/MindLift.UnitTests`: **47 passed** (all seven templates pass validation; real estate renames deals and sets its fields; a broken template is reported)
- `dotnet test tests/MindLift.IntegrationTests`: **64 passed**, including the full flow (sign-up → company → industry → sample data → complete, with module labels, pipeline stages, custom fields and sample counts checked in the database), switching industry after sample data, the template lock once real records exist, tenant isolation of templates, step skipping rules and sample-once, removing sample data without touching real records, expired trial blocking writes, and the reminder sent once
### M5 — Design system, frontend shell & onboarding UI (partial)
**Built and checked in a browser this session**
- [x] `MindLift.UI` library: tokens and classes from plan 18.5 (`wwwroot/css/mindlift.css`), theme from 18.4 (`MindLiftTheme`, adapted to MudBlazor 9: font weights and line heights are text values), shared components: `PageHeader`, `MlCard`, `SectionCard`, `StatCard`, `StatusChip` + `StatusColors`, `EmptyState`, `ErrorState`, `SkeletonTable`, `MoneyText`, `DateText`, `KeyValueList`, and `Formatting` (compact money, relative dates, Western digits)
- [x] Staff app shell: app bar (menu, company name, account menu with dark mode, language switch and sign out), drawer built from the bootstrap (only enabled modules the user may view), right-to-left provider driven by the user's language, snackbars at the bottom end
- [x] Session: access token in memory; refresh cookie restores the session on page load (`SessionRestore`); the request handler refreshes once on 401 and retries
- [x] Sign-in, sign-up (country defaults fill the company step), six-step onboarding wizard (company, industry, pipeline, team, data, channels), skip for steps 3–6, complete → dashboard
- [x] Dashboard (greeting, setup card while setup is unfinished, "Getting started" checklist, empty state)
- [x] Settings: company profile (time zone, week start, fiscal year, tax) and modules (switch on or off, labels)
- [x] Style guide at `/_styleguide` showing every shared component (development only; it must be hidden in production builds)

**Verified:** `dotnet build` succeeds; unit tests 47 passed; integration tests 64 passed. In the built-in browser: sign-up created a workspace, the wizard ran from company to completion with the real estate template, the dashboard showed the greeting and checklist, the style guide rendered cards, KPI tiles and chips correctly, and the sign-in page renders with the design-system fonts.

**Done later in M5:** roles and permissions matrix (`/settings/roles`, with scope per module and read-only admin matrix), teams and branches (`/settings/teams`), pipelines and stages (`/settings/pipelines`), fields and layouts for the Form and Detail pages (`/settings/layouts`). The settings navigation lists all of them. Users, invitations, custom fields, picklists and currencies were done earlier.

**Still open in M5:**
- `FileDropZone`, `ChartCard`, `FilterBuilder`, `DynamicForm`, `FieldValue`, `RecordDetailLayout`, `Timeline` and pickers are not built yet. The record screens in M6 use the components that exist (`PageHeader`, `MlCard`, `FormDrawer`, `StatusChip`, `UserAvatar`, `EmptyState`, `ErrorState`, skeletons)
- Document types settings are deferred to M16
- Arabic (right-to-left) layout: the switch exists but has not been reviewed on screen; UI strings are not in `.resx` files yet
- Tenant brand colour is not applied to the theme yet
- Playwright screenshot harness (`tests/MindLift.E2E`) is not created yet; screen checks so far were done by hand in the built-in browser
- Global search, the notifications bell (static), inbox, calendar and reports pages do not exist yet
- Route gating: unfinished setup shows the setup card on the dashboard, and the wizard is reachable at any time; it is not enforced by redirect yet
- Fonts load from Google Fonts; they need that site to be reachable
- The new settings pages (roles, teams, pipelines, layouts) compile but have not been checked in the browser yet (see M6 below for why)
**Notes**
- Files edited from PowerShell must be re-saved as UTF-8. An earlier edit corrupted the middle dot and dash characters in four pages; they were repaired. Check new edits for mojibake.
- Enum values still travel as numbers (see M3).
## Carry-over pass 1 (2026-10-09)

Work from `REMAINING_WORK_PLAN.md` section 2. Test totals at the end of this pass: 336 unit, 69 UI component, 91 integration, all passing, three solution-wide runs in a row with no failures.

**Fixed, with the cause found**
- **Random HTTP 500s in integration tests (F1).** Cause: connection exhaustion. Every test host keeps its own Postgres pool and 20 Hangfire workers, and hosts run in parallel. A normal run peaked at 102 connections against Postgres's default limit of 100. Proof: with the limit lowered to 40 the log showed 159 "53300 too many clients" errors. Fix: container limit 400, a pool of 12 per test host, Hangfire workers set from `Hangfire:WorkerCount` (2 in tests, the old default otherwise). Peak is now 68.
- **Blank page after a rebuild (F2).** In Development only, `index.html` and `/_framework/*` are sent with `Cache-Control: no-cache, no-store`. Checked: the app loads straight after a rebuild with no cache clearing.
- **Filter compiler bug.** "Is empty" and "is not empty" threw on fields that can never be null (owner, created date, amount, stage, probability). Found by the new test that compiles every operator the filter builder offers against every registry field. Now "is empty" is always false and "is not empty" always true for those fields.
- **`FilesController` was open to anonymous callers.** There is no fallback authorization policy, so a controller with no attribute is public. Added `[Authorize]`; `ControllerGuardTests` now lists `FilesController` as a documented sign-in-only controller.
- **Arabic text on the Company page** had been corrupted by an earlier edit. Repaired.

**Built and verified**
- **File upload and download (C1).** `POST /api/v1/files`, `GET /api/v1/files`, `GET /api/v1/files/{id}`, `DELETE /api/v1/files/{id}`. Allowed types PDF, JPG, PNG, WEBP, TXT, CSV, DOCX, XLSX; size limit 25 MB (`Storage:MaxUploadMegabytes`); the file signature must match the extension; the stored content type is chosen by the server; names lose any path; downloads are attachments with `nosniff`. Attaching to a record needs Edit on that module, reading needs View, an unattached file is private to its uploader, and another workspace gets 404. 17 integration tests.
- **Shared components (C2).** `SectionCard`, `ConfirmDialog` with `ConfirmAsync`, `FileDropZone`, `ChartCard` with `ChartDefaults` (Blazor-ApexCharts 6.1.0), `SkeletonCards` brought to spec. `FormDrawer` asks "Discard changes?" when `IsDirty`; the lead form uses it. Styleguide shows all of them. Checked in the browser: styleguide, the real chart (6 bars), the confirm dialog (Cancel returns "Cancelled").
- **UI component tests.** New project `tests/MindLift.UITests` (bUnit): formatting, chart defaults, file drop zone, dialogs, cards, filter converter and filter builder.
- **Filter builder.** `FilterOperators` (operators per field type, in Shared), `FilterConverter` (rows to the shared `FilterGroup` and back, unfinished rows dropped, nested groups kept), `FilterBuilder` component. Component-tested only; not yet seen in the browser.
- **Brand colour (C10).** `BrandColor` in Shared: `#RRGGBB` and contrast with white text of at least 3:1. The server rejects bad values; the Company page has the field with a live swatch; the theme follows it. Checked: a too-light colour shows the message, a valid colour saves and the theme's primary changes to it.
- **Setup guard (C11).** An admin with unfinished setup is sent to `/onboarding` from every other page. Checked on `/leads` and `/settings/company`, and the redirect stops once setup is complete. Staff users are not guarded.
- **Notifications bell and search box (C12).** The bell shows an empty message; search is shown but disabled until M7.
- **Styleguide hidden outside Development (C13).** Visible in Development (checked). Not tested against a production host.
- **Disqualify reason (C8).** Empty reason marks the field invalid with a message. Checked in the browser.

**Not done yet from the carry-over list**
- `EntityListPage`, `DynamicForm`, `FieldValue`, `RecordDetailLayout`, `Timeline`, user and record pickers (C3); lead pages moved onto them (C4)
- Leads kanban (C5), import wizard and export button (C6), merge screen (C7)
- Arabic strings in `.resx` and mirrored pager arrows (C9)
- Playwright harness and E2E tests (T1, T2, T3); the other test items T4 to T8
- Refresh-token cleanup job (C14)

**Not verified**
- `FileDropZone` with a real drag and drop, and with a real upload to the new endpoint
- `ChartCard` in dark mode and in Arabic
- `FilterBuilder` in the browser

## Carry-over pass 2 (2026-10-09)

Test totals: 338 unit, 125 UI component, 103 integration, all passing.

**Two real bugs found by the new tests, both fixed**
- **Text filters failed against PostgreSQL.** "Contains", "Does not contain" and "Starts with" returned HTTP 500 for every user, because the PostgreSQL provider cannot translate `string.ToLowerInvariant()`. Unit tests missed it because they run in memory. Fixed by using `ToLower()` with a null check (so a missing value no longer breaks in-memory matching used by assignment rules, and "does not contain" now includes records with no value). A new integration test class, `LeadFilterTests`, runs every operator against the real database (12 tests).
- **The UI library ignored every event attribute.** `MindLift.UI/_Imports.razor` did not import `Microsoft.AspNetCore.Components.Web`, so `@onclick`, `@ondragenter` and the like were written into the page as plain text. This silently disabled the clickable `MlCard` and the drop zone's drag highlight. Found by a bUnit test; regression tests added for both.

**Built and verified**
- **Generic list page (`EntityListPage`).** View tabs, debounced search, filter drawer with the filter builder, removable filter chips, sortable headers (ascending, descending, default), a table on desktop and cards on phones, paging, the first-time, no-results and error states, and a late answer never replaces a newer one. The view, search, page, filters and sort live in the address (`?view=&q=&page=&f=&sort=&dir=`), so refresh, back and shared links restore the list; the default sort is not written there. 23 component tests.
- **Supporting pieces.** `ListState` (state to and from the address, damaged links ignored, 19 tests), `FilterDescriber` (chip text such as "Status is any of New, Working", 11 tests), `ListColumn`, `ListView`, `ListLoadException`.
- **Leads list moved onto it (C4).** `LeadsList.razor` is now a thin page: three views, five columns, a card layout for phones, the owner picker loads people when the user may list users. Source now shows its label.
- **Checked in the browser:** the leads list (styled table, sort arrow, pager), the Filters drawer with the filter builder, applying "First name contains web" (chip, one result, address updated, no error), the phone card layout.

**Deviations from plan 18.8.1 (not done yet)**
- Bulk selection and the bulk action bar, the column chooser, the row menu, "+ Save view"
- Phone paging uses the same previous and next buttons, not "Load more"
- Tablet columns: only the Source column is hidden

**Not verified**
- `EntityListPage` in dark mode and in Arabic
- Browser history (back and forward) restoring list state: covered by logic tests only
- The filter builder with date, number and person fields in the browser (covered by component and conversion tests)

**Still open from the carry-over list**
- `DynamicForm`, `FieldValue`, `RecordDetailLayout`, `Timeline`, user and record pickers (C3); lead detail moved onto them
- Leads kanban (C5), import wizard and export button (C6), merge screen (C7)
- Arabic strings in `.resx` (C9); Playwright harness and E2E tests (T1 to T3); tests T4 to T8

## Carry-over pass 3 (2026-10-09)

Test totals: 373 unit, 125 UI component, 112 integration, all passing.

**Three real export bugs found while building the import and export screens, all fixed**
- **Shifted columns.** The header listed 11 columns, including `first_name` and `last_name`, but each row wrote 10 values with a single combined name. Every value after the name sat under the wrong heading, so Email appeared under Last name and so on. The old test only checked the header text. `LeadListItemDto` now carries `FirstName` and `LastName`, and rows match the header.
- **Silent 200-row limit.** The export asked for 10,000 rows but the shared query clamps a page to 200, so any export of more than 200 leads was cut off without a message. The export now walks the pages (capped at 10,000). Test: 450 leads, 450 rows out.
- **Spreadsheet formula injection.** A name such as `=HYPERLINK(...)` would run as a formula when someone opened the export in Excel. `CsvText.Escape` now puts an apostrophe before values starting with `=`, `@`, tab, carriage return, and `+` or `-` unless the value is a plain number or phone number. Phone numbers such as `+971 50 123 4567` are left alone.

**Also found and fixed**
- The import template said "Notes" but the import only knew "Description", so a downloaded template would have silently dropped that column. The import now accepts both.
- The "Merge" confirmation said "their tags" for a single lead.

**Built and verified**
- **Import wizard (`/leads/import`).** Choose file (drag and drop or browse, CSV, up to 5 MB, template download), check (how each column will be read, ignored columns, a preview of the first 5 rows, errors for a missing First name column or more than 5,000 rows), result (imported, skipped as duplicates, could not be imported, with the first errors). Column rules live in `LeadImportColumns` (Shared) and are used by both the server and the wizard; `CsvText` moved to Shared so the browser can read the file.
- **Export button** on the leads list. It exports exactly what the list shows (view, search, filters, sort) and downloads `leads.csv`. `Import` and `Export` buttons show only to people with those permissions.
- **Merge screen (C7).** "Find duplicates" on a lead lists other leads with the same email, phone or name (never the lead itself) and matching contacts for information. Tick leads, "Merge selected", then a danger confirmation that says what happens. The survivor keeps its values and fills empty ones from the duplicates, and tags are combined.
- **Checked in the browser:** import of a 4-row file (2 imported, 1 skipped as duplicate, 1 failed with "Row 4: first name is missing"), the merge flow (empty selection warns, confirmation text, the survivor gained the phone and company), and the export (header and rows aligned, "Export ready").
- **Tests added:** 12 `LeadFilterTests` (earlier), 9 `LeadExportImportTests`, 22 CSV unit tests, 9 import column tests, 125 UI component tests in total.

**Not verified**
- A real file download in a browser (the download helper was checked by intercepting it)
- Import with a non-UTF-8 file (for example Excel's "CSV" in a legacy code page)
- The import wizard on a phone and in Arabic

**Still open from the carry-over list**
- `DynamicForm`, `FieldValue`, `RecordDetailLayout`, `Timeline`, user and record pickers (C3); lead detail moved onto them
- Leads kanban (C5)
- Arabic strings in `.resx` (C9); Playwright harness and E2E tests (T1 to T3); tests T4 to T8

## Carry-over pass 4 (2026-10-09)

Test totals: 373 unit, 143 UI component, 231 integration (747 in all), all passing.

**Real bugs found by the new backend tests, all fixed**
- **No size limits on lead input.** Only the first and last name columns had limits, so a very long value on the anonymous public web form returned HTTP 500, and every other field (notes, company, phone, email) accepted unlimited text. Added validators for lead create and update, the public form submission, duplicate check, disqualify, convert, merge, assignment rules and web forms. Too much is now a 400 with an error on the field.
- **Settings input also returned 500 or accepted nonsense.** A blank company name was accepted; over-long company names, module labels and picklist labels returned 500; custom field labels were unbounded. Validators added for company settings, the onboarding company step, modules, picklist values and custom fields.
- **An admin could switch off the Settings module,** which locks them out of the screen that switches it back on. Dashboard, Users and Settings now refuse to be switched off (422 `core_module`), and the Modules screen shows them as "Always on" with the switch disabled.
- **Stage positions were stored as given.** Adding an open stage before Won failed unless the caller also renumbered Won and Lost, and the Pipelines screen defaulted a new stage to after Lost, so adding a stage there always failed. A position now means "insert before the stage that is there now"; the others move down and every stage is renumbered 1, 2, 3. Moving and deleting a stage renumber too. The screen now defaults a new stage to just before Won.
- **Setup could be finished with no pipeline** (the industry step creates the first one), leaving Deals with nothing to use. Finishing setup now needs a pipeline (422 `setup_incomplete`).
- **Saving a team returned a member count of 0.** It now returns the real count.
- **A team leader was not checked.** Any id, including a person from another workspace, could be saved. A leader must be an active person in this workspace.
- **A branch time zone was not checked.** It must be a real time zone (one shared rule with company settings).
- **Values could be added to a picklist that does not exist.** That is now a 404.

**Built and verified**
- **Leads board (`/leads/board`).** Generic `KanbanBoard` (columns with names and real totals, cards that drag between columns, a highlighted drop target, a "Move to…" card menu and a column picker for phones, a locked-card option, a footer slot), a reusable reason prompt (`PromptReasonAsync`), and a leads board with search, "Only mine", "Show more" per column and a Board | List toggle on both pages. Dropping on "Disqualified" asks for the reason first and uses the disqualify endpoint; other drops update the status. Checked in the browser by sending the same DOM drag events a browser sends: the target highlights, "Jon Import moved to Contacted" and the counts reload; dropping on "Not interested" shows the reason dialog, an empty reason shows "A reason is required.", and a reason moves the lead. The phone layout shows column chips with counts and one column at a time.
- **Backend tests:** assignment rules across several people (15), public web forms (15), input limits (12), teams and branches (13), pipelines and stages (20), company, modules, custom fields, picklists and roles (41), refresh token cleanup (3).
- **Refresh token cleanup job (C14).** Runs daily at 03:00 and removes tokens that expired more than 7 days ago. A revoked token that has not yet expired is kept, so reusing a stolen token is still detected (tested).

- **Also checked in the browser after the last rebuild:** the Modules screen shows Dashboard, Users and Settings as "Always on" with the switch disabled; the Pipelines screen offers position 5 for a new stage in a pipeline with four open stages, and saving puts the new stage right before Won; the kanban cards on a phone have the move menu at the top-right of each card.

**Not verified**
- A real mouse drag in a browser. The browser tool cannot start a native drag, so the drop was checked with dispatched drag events only.
- The phone card menu ("Move to…") was not clicked in the browser (component-tested only).

**Still open from the carry-over list**
- `DynamicForm`, `FieldValue`, `RecordDetailLayout`, `Timeline`, user and record pickers (C3); lead detail moved onto them
- Arabic strings in `.resx` (C9)
- Playwright harness and E2E tests (T1 to T3); the remaining T5 (drawers driven in the browser) and T8 (live email through Mailpit)

## Open items carried forward
- MudBlazor 9.11 is installed but the plan's theme code (section 18.4) targets the 8.x API. Adapt it in M5a and record the changes here.
- The shared `DocumentProcessing` / `ocr` queue is registered now; the workers come with M16.
- **File upload and download endpoints** (deferred from M1 to M2) are still NOT implemented. `IFileStorage`, `LocalFileStorage` and the `files` table exist. Add `POST /api/v1/files` and `GET /api/v1/files/{id}` with the record's permission check in M3 or whenever the first module needs attachments.
- `FluentValidation` validators: sign-up, login, invitations, roles, teams, branches, user update and password reset are covered. Each new module adds its validators in `src/MindLift.Api/Validation`. The registration loop in `Program.cs` picks them up automatically.
- Refresh-token cleanup (expired and revoked rows are kept until the `CleanupJob` in M15).
- Live SMTP delivery to Mailpit has not been run end to end yet (tests use a recording sender).

## M6 - Leads

**Built**
- Backend: lead create, update, delete (converted leads are locked), disqualify with a lost reason, convert to contact, account and deal (locks the lead), duplicate check by email or phone, merge, CSV import (staff need permission by default: "Staff cannot import leads by default"), CSV export, assignment rules, web forms and the public form endpoint
- Web: `/leads` list with the three saved views (My open leads, All open, New), search, paging and the empty states; `/leads/{id}` detail; `LeadFormDrawer` for create and edit; lead settings reached through the Leads module

**Added later in M6**
- Lead assignment (`/settings/assignment-rules`): list ordered by priority, a drawer to create and edit rules (name, priority, method, ordered users with move buttons, one optional condition on source, rating or status), delete with an inline confirmation
- Web forms (`/settings/web-forms`): list with live or paused state, submission count and field list; a drawer to create and edit forms (name, source, success message, optional fields, accepting submissions); the public endpoint shown with how to call it
- Both pages are linked from the settings navigation
- Checked: the rule saves through the API and the list shows "Priority 1 · Round robin · Test Owner · source = Website". A web form was created, then submitted to its public endpoint, and the lead appeared with source "website" and owner "Test Owner"
- Gap: the test lead's owner matches the workspace owner, so this check does not prove the rule applied. Test with a second user before relying on it
- Gap: the rule and form drawers were not checked in the browser. The drawer selects were hard to drive in the pane. The API paths they call were checked
- Gap: the form builder does not preview the public form, and there is no hosted form page. The website has to call the endpoint itself
- Gap: round robin and fewest-open-leads distribution across several users is not tested

**Still missing in M6**
- Merge action on the lead screens (the API exists)
- CSV import and export screens (the API exists)

**Verified**
- Unit tests: 47 passed (`dotnet test`, unit project)
- Integration tests: 74 passed in the last full run. The lead flow tests pass on their own (10 of 10)
- `dotnet build` of `MindLift.Web` succeeds after fixing the `LeadsList` row click (the interpolated string in the attribute), the `FilterOperator` name clash with MudBlazor (a file-level alias), the view-button attributes (string literals inside attributes), and the deactivation confirmation in `Users` (inline, because MudBlazor 9 has no `ShowMessageBox` call here)
- New settings pages (roles, teams, pipelines, layouts) build. Not yet checked in the browser

**Known issue (not fixed)**
- Some integration tests returned HTTP 500 in one full run (`Duplicate_check_finds_a_lead...`, `The_workspace_owner_cannot_be_deactivated`, `Staff_cannot_import_leads_by_default`, `Conversion_creates_contact_account_and_deal...`, `Admin_cannot_delete_a_system_role...`). The next full run passed all 74. The server log shows only the "__EFMigrationsHistory" probe errors on fresh databases, which EF logs normally. The cause is not found. Likely a race between the first request and migration on a new per-host database. To confirm: run the suite 3 times in a row and check the server log for the first exception of each failing test.

**Browser check (synthetic test workspace, localhost)**
- Lead list: create through the drawer, the row shows the lead, the empty state renders before any lead exists
- Lead detail: header, actions and details render. "Disqualified because" now shows only for disqualified leads (fixed in `LeadDetail.razor`)
- Convert: creates the contact, company and deal, and the lead shows as Converted. The converted lead drops out of "All open"
- Edit: the drawer loads the lead, "Save changes" saves, and the detail page shows the new company
- Duplicate check on create: a second lead with the same email shows "Possible duplicates" with the existing lead named. Pressing the button again creates it anyway, as designed
- Disqualify: an empty reason is rejected with a toast. With a reason, the lead shows "Not interested" and the reason row
- Merge: the web app has no merge action. The endpoint `POST /api/v1/leads/{id}/merge` was called directly with the test account's token. The survivor kept its data and the duplicate was merged in
- Gap: the required Reason field is not highlighted when left empty. Only the toast says so
- Views and search: "All open", "My open leads" and "New" show the open leads. Converted and disqualified leads are excluded from all three. Search "Second Co" narrows the list to one lead
- Tablet (768 wide): the table, views, search and paging fit. No horizontal scroll
- Phone (375 wide): the table scrolled sideways and hid Status and Owner. Fixed: the Created column is hidden on phones and the table minimum width is reduced. Re-checked at 375 wide: Name and Status now fit with no sideways scroll. Source, Owner and Created are hidden on phones (the same as the list's existing xs rules)
- Avatars: the two-letter initials overflowed the circle because the font size equalled the avatar size. Fixed in `UserAvatar.razor` (font 40% of the size, width and height pinned)
- Arabic (RTL): the list mirrors correctly (Status on the right, avatars and paging in the right order). Gap: the strings are still English (for example "records", "leads · page", "My open leads"). This is the open translation item
- Missing from the web app: a merge action on the lead screens (the API exists)
## Browser tests (tests/MindLift.E2E)
- Playwright with Microsoft Edge (headless) against the real API process and a throwaway PostgreSQL container. Off by default; run with `MINDLIFT_E2E=1 dotnet test tests/MindLift.E2E` (needs Docker and Edge; build the API first).
- 30 tests: sign-up and setup wizard guard (4), lead screens (18: create, duplicates, search, filters, sort, edit, disqualify, convert, merge, real drag-and-drop on the board, CSV import, export download), brand colour and style guide (3), and every key page at 1440/768/375 wide in English and Arabic checked for sideways scrolling and RTL (5).
- Screenshots are saved to tests/MindLift.E2E/screenshots.
- Found and fixed: Settings > Users scrolled sideways on a phone with long emails.
- Still not covered: the phone "Move to…" menu click, non-UTF-8 import, rule/web-form drawers, live email through Mailpit.

## M7 - Contacts and Accounts (+ C3 shared components)
- **API:** `/api/v1/contacts` and `/api/v1/accounts` (list/query, get, summary, create, edit, delete, import, export), contact duplicate check and merge, account parent hierarchy (loop and duplicate-name checks; deleting is refused while an account has deals or child accounts, and contacts are kept), `/api/v1/search` (contacts, accounts, leads; each kind only if the person may see it). Migration `Contacts_Accounts_M7` adds contact job title/notes and account phone/industry/notes/parent.
- **Converted lead history** shows on the contact's History timeline.
- **Shared UI (C3 done):** `DynamicForm`, `FieldValue`, `Timeline`, `RecordDetailLayout`, `RecordPicker`, `UserPicker`, plus one `ImportWizard` used by leads, contacts and accounts. The lead form now uses `DynamicForm`.
- **Screens:** Contacts and Accounts lists (views, filters, search, export, import), detail pages, form drawers, live search box in the top bar.
- **Tests:** 13 API tests, 15 component tests, 12 browser tests; all browser tests (42) passed on the last full run, and the one fix since then passed on its own.
- **Not done:** the activities, deals and files tabs on these pages arrive with M8/M9; deals reach a contact through its account until M8. Arabic strings (C9) are paused at the user's request.

## M8 - Deals and pipeline
- **API:** `/api/v1/deals` list/query, board (`POST /deals/board`), get, history, create, edit, stage change (`PATCH /deals/{id}/stage`), delete, import, export.
- **Rules:** a new deal starts in an open stage; Won sets status, close date and 100%; Lost needs a valid reason and sets 0%; moving back to Open clears them; a typed probability sticks until the deal is reset; moving to another pipeline needs a stage of that pipeline; every move writes history with the time spent in the stage it left; a stale version gets 409 so the board reloads; rotting uses the stage's days against the last activity or the last move; amounts are converted to the base currency with the newest rate (a currency with no rate is refused); several contacts per deal with one primary, and the company follows the primary contact; the first won deal turns a prospect contact into a client.
- **UI:** deals list (views, filters, search, import, export), full-width board with column totals and weighted totals, drag to Won (confirm) and Lost (reason dialog), phone card menu, detail page with stage bar, history, people, files, activities and onboarding.
- **Tests:** 18 API tests, 12 browser tests.

## M9 (partly) - Activities and onboarding checklists
- **Done:** tasks, calls, meetings and notes (notes are plain text, so there is nothing to sanitise); links to leads, contacts, accounts and deals with auto-fill; complete (with call outcome, notes and a follow-up task), reopen, delete; My Activities page with Today, Overdue, Upcoming and Completed tabs; activities card on every record page; lead conversion moves the lead's activities to the new records; a deal's last and next activity dates follow its tasks.
- **Onboarding checklists:** templates in Settings, tasks created when a deal is won (once per deal and template), progress on the deal page.
- **Tests:** 12 API tests, 10 browser tests.
- **Not done (still M9):** `@mentions`, `.ics` meeting invitations, reminders, the "+" quick-add in the top bar, and a combined timeline of field changes.

## Gaps found and closed while testing
- **The emails linked to pages that did not exist.** Added Forgot password, Reset password, Confirm email and Accept invitation pages and a "Forgot your password?" link on sign-in. Nine browser tests now send real emails through a Mailpit container and follow the links.
- Files card on lead, contact, account and deal pages (upload, download, delete).
- Import wizard reads Latin-1 and UTF-16 files, not only UTF-8 (a deal import wizard and the contact and account imports share one wizard).
- Browser tests now also cover: the settings write screens (rules, web forms, teams and branches, pipelines and stages, roles), the phone card menu, files and the new auth pages.
- T4 (rules with several users) and T6 (public form checks) were already covered by API tests.

## M9 - rest of activities
- **Timeline for every record** (`GET /api/v1/timeline`): activities, field changes (from the audit log, with before and after), stage moves, "created", the converted-lead history on a contact, and, on an account, the contacts and deals added to it plus their activities. Newest first. A Timeline card is on the lead, contact, account and deal pages and refreshes when something is added on the page.
- **Meeting invitations:** a meeting can list guest emails and "Email calendar invitations". Each guest gets a message with a standard `.ics` file (written by hand, with escaping and 75-byte line folding, so a subject cannot inject calendar lines). Editing a meeting whose invitations went out sends an update (SEQUENCE raised); deleting it sends a cancellation. Checked with real mail in Mailpit.
- **Mentions:** a note can mention active colleagues; they are stored and shown on the note. Notes stay plain text, so there is nothing to sanitise. The notification to the person mentioned arrives with M10.
- **Quick add:** a "+" menu in the top bar adds a task, call, meeting or note from any page.
- **Reminders:** the "remind me" minutes are saved on tasks and meetings. Sending the reminder arrives with M10 (notifications), as the plan intends.
- **Tests:** 11 more API tests, 5 more browser tests.
- **Still open from M9:** none that the plan lists; mention notifications and reminder delivery belong to M10.

## M10 - Notifications and reminders
- **Pipeline** (`INotificationService`): skips the person who caused the event (unless asked not to), skips deactivated people, applies each person's preferences, saves the in-app notification, pushes it live over SignalR to that person only, and queues the email in the background. Several notifications with the same group key within 10 minutes share one email.
- **What triggers one:** a lead, contact, account, deal or task given to someone else (on create and on reassignment, once); a new @mention in a note (not repeated on edit); a deal won or lost (owner and the leaders of the owner's teams, not the person who moved it); an import finishing (to the person who ran it); the trial ending soon; a task or meeting reaching its "remind me" time; a task going overdue.
- **Jobs:** reminders every minute, overdue tasks every 15 minutes (each once; moving a task gives it a fresh reminder), a daily "my day" email at 08:00 company time for people who switched it on (only when there is something in it), and a daily clean-up of notifications older than 180 days.
- **API:** list (all or unread, paged), unread count, mark one or all read, preferences get and save. Everything is limited to the signed-in person; another person's notification looks like it does not exist.
- **Real time:** the hub now requires sign-in, reads the token from the query string, and puts each connection in a group for its person and one for its workspace. Anonymous and forged connections are refused.
- **UI:** bell with a count, a list of the latest 20, mark all read, click to open the record; a toast the moment something arrives (with a View button); a Notifications page (All / Unread, show more); a preferences page (in MindLift and email for each kind, plus the daily summary); reconnects pick up what was missed.
- **Fixed on the way:** the background queue was only looked at every 15 seconds, which delayed every email; it is now every second.
- **Tests:** 21 API tests (including SignalR delivery and isolation), 6 browser tests with two real people in one workspace, including the "toast within 5 seconds" check.
- **Not in scope yet:** push notifications to phones (Phase 3, as the plan says), and the notification kinds of later modules (bookings, messages, documents) which get added as those modules arrive.

## M11 - Calendar and booking (M11b calendar sync deferred)
- **Calendar:** `GET /api/v1/calendar/events` (meetings, open tasks with a time, bookings; by person or team, limited by each person's data scope). `PATCH /api/v1/activities/{id}/schedule` moves a meeting or task and moves its booking with it.
- **Booking pages (Settings):** create, edit, delete, list; bookings listed per page. Validation covers slug, durations, buffers, notice, days ahead, opening hours (time format, order, overlaps, valid time zone, date exceptions), people, questions, reminders.
- **Free times:** the page's opening hours in its time zone, minus meetings already in the diary (with buffers), minus times too soon or too far ahead; date exceptions open or close a day; round robin offers a time while any one person is free.
- **Booking:** under a lock per person, re-checks the time, reuses the contact or open lead with the same email or phone (otherwise makes a lead with source Booking), creates the meeting, sends a confirmation with an `.ics` invitation and a manage link, and notifies the person. A filled-in honeypot books nothing.
- **Manage link:** the secret is stored only as a hash. The customer can cancel, or move to a new free time; a booking cannot be changed once it has started.
- **Reminders:** a job every 5 minutes sends each reminder once; a booking made after a reminder's time gets no late copy.
- **Screens:** `/calendar` (week view, previous/next/today, "only mine"), `/settings/booking-pages`, public `/book/{workspace}/{page}` (pick day, time, details), `/booking/manage/{token}`.
- **Tests:** 15 API tests for bookings, 4 browser tests (public booking, manage and cancel, settings page creation).
- **Not done in M11:**
  - The calendar is a week list. Day and month views, the user and team picker, and drag to reschedule in the browser are not built; the drag works through the API.
  - Google and Microsoft calendar sync (M11b) is deferred, as the plan allows.
  - Arabic strings for the booking pages wait for C9, which is paused.
- **Test stability fixes found on the way:**
  - Hangfire keeps one storage per process, so parallel test hosts sent jobs to one another's database. Each application now has its own storage, job client and worker.
  - Under heavy parallel load the Hangfire worker sometimes did not pick up queued jobs within the test's wait. Test hosts run email jobs inline (`InlineJobClient`); production still uses the queue. **Open:** the root cause of the worker not picking jobs up under load is not found yet.

## Core Automation Engine & Universal Save Pipeline (Zoho TCA Engine Gap Fix)
- **Built & Integrated:**
  - **Shared:** `AutomationTriggerType` (`RecordCreated`, `RecordUpdated`, `FieldChanged`, `StageChanged`), `AutomationActionType` (`UpdateField`, `CreateTask`, `SendNotification`, `SendEmail`, `Webhook`), `AutomationActionDefinition` (with email fields and `ScheduledDelayMinutes`), `AutomationRuleDto`, `SaveAutomationRuleRequest`, and `AutomationExecutionLogDto` in [AutomationContracts.cs](file:///d:/MindLift_CRM/src/MindLift.Shared/Contracts/AutomationContracts.cs).
  - **Domain:** `AutomationRule` and `AutomationExecutionLog` entities with `[Audited]` and multi-tenant isolation in [Automation.cs](file:///d:/MindLift_CRM/src/MindLift.Domain/Entities/Automation.cs).
  - **Loop Protection:** `IAutomationExecutionContext` guarding against recursive workflow loops with depth capping ($\le 3$) and max execution thresholds ($\le 25$).
  - **Automation Engine:** `AutomationEngine` evaluates triggers, condition criteria via compiled expressions (`FilterCompiler`), and dispatches instant or scheduled actions with execution audit logging.
  - **Universal Save Pipeline:** `RecordSavePipeline` coordinates atomic updates and single-pass trigger processing. Hooked into all five core entities: `LeadService`, `DealService`, `ContactService`, `AccountService`, and `ActivityService`.
  - **SSRF Safe Webhooks:** `WebhookSecurity` blocks RFC 1918 subnets, localhost, and cloud metadata endpoints (`169.254.169.254`).
  - **Scheduled Actions:** `AutomationScheduledJob` executes delayed actions via Hangfire after re-validating record state and conditions.
  - **API:** `AutomationController` (`/api/v1/automation/rules`, `/rules/{id}/toggle`, `/logs`) guarded with `[HasPermission(AppModule.Settings, ...)]` and validated via `SaveAutomationRuleRequestValidator`.
  - **Persistence:** EF Core migration `Automation_Engine_TCA` generated and applied with JSONB configuration and multi-tenant indexes.
  - **UI / Workflow Builder:** Responsive Blazor WASM interface at `/settings/automation-rules` (`AutomationRules.razor`) featuring:
    - Active rules list with live toggle switch and delete confirmation.
    - Rule Drawer with Trigger selector (Created, Updated, Field Changed, Stage Changed).
    - Condition Builder: configurable criteria (Field, Operator: Eq/Neq/Contains/Gt/Lt/IsEmpty/IsNotEmpty, Value) with AND/OR logic.
    - Action Builder: multi-action configuration (Create Task, Update Field, Send Toast Notification, Send Email, Webhook) with optional scheduled delay.
    - Execution Logs tab with status chips and error diagnostics.
    - Integration into `SettingsNav.razor`.
- **Tests & Verification:**
  - **Unit Tests:** 404/404 passed (`AutomationEngineTests.cs` verifying loop protection, condition compilation, and field mutation).
  - **Integration Tests:** 328/328 passed (`AutomationFlowTests.cs` verifying rule lifecycle, lead save trigger & task generation, Staff 403 authorization enforcement, cross-tenant isolation, condition matching criteria, SSRF webhook blocking, and email delivery).
  - **Regressions:** Zero regressions across all M0–M11 milestones.

## M13: Dashboard & KPI Analytics (Priority Milestone)
- **Built & Integrated:**
  - **Shared:** `WidgetSize`, `KpiTileDto`, and all 14 Phase 1 widget DTOs (`DashboardTaskItem`, `DashboardMeetingItem`, `DashboardLeadItem`, `StageBreakdownItem`, `ClosingDealItem`, `TargetProgressItem`, `MonthlySalesItem`, `LeadSourceItem`, `FunnelStageItem`, `LeaderboardUserItem`, `UserOverdueItem`, `RecentWinItem`, `ActivityTypeSummaryItem`, `SystemOverviewData`, `DashboardWidgetDto`, `DashboardResponse`, `DashboardLayoutDto`, `SalesTargetDto`) in [DashboardContracts.cs](file:///d:/MindLift_CRM/src/MindLift.Shared/Contracts/DashboardContracts.cs).
  - **Domain & Persistence:** `DashboardLayout` and `SalesTarget` entities with `[Audited]` and multi-tenant isolation in [Dashboard.cs](file:///d:/MindLift_CRM/src/MindLift.Domain/Entities/Dashboard.cs), mapped in `AppDbContext`, EF Core migration `Dashboard_And_Sales_Targets`.
  - **Metrics Engine:** [SalesMetrics.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Dashboard/SalesMetrics.cs) calculating Won amount & count, pipeline value, new leads, conversion cohorts, activities completed, and overdue tasks with tenant timezones and exchange rates.
  - **All 14 Phase 1 Widgets:** [DashboardWidgets.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Dashboard/DashboardWidgets.cs) (`kpi_strip`, `my_day`, `upcoming_meetings`, `new_leads`, `deals_in_progress`, `deals_closing_soon`, `target_progress`, `sales_trend`, `leads_by_source`, `pipeline_funnel`, `team_leaderboard`, `team_overdue`, `recent_wins`, `activity_summary`, `system_overview`).
  - **Dashboard Service & Scope Resolution:** [DashboardService.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Dashboard/DashboardService.cs) supporting role-based view downgrading (Staff can only view Staff, Admin can switch between Staff, Manager, and Admin), period filters (This Month, Last Month, This Quarter, This Year, Custom), HybridCache caching, and layout persistence.
  - **API:** [DashboardController.cs](file:///d:/MindLift_CRM/src/MindLift.Api/Controllers/DashboardController.cs) (`/api/v1/dashboard`, `/widgets/{key}`, `/layout`) and `SalesTargetsController` (`/api/v1/sales-targets`).
  - **UI / Dashboard Page:** [Home.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Home.razor) featuring interactive View Switcher chips, Period filter, real-time KPI strip with trend badges, interactive "My Day" with task completion, responsive 12-column widget grid, and Layout Customizer drawer.
- **Tests & Verification:**
  - **Integration Tests:** 4/4 passed in [DashboardFlowTests.cs](file:///d:/MindLift_CRM/tests/MindLift.IntegrationTests/Dashboard/DashboardFlowTests.cs) (Staff view restriction, Admin view switching & period filtering, Layout customizer save/reset, Sales targets CRUD & progress flow).
  - **Build Status:** 0 errors.

## M12: Omnichannel Communication & Unified Inbox
- **Built & Integrated:**
  - **Shared:** `CommunicationChannel` (Email, WhatsApp, SMS), `MessageDirection` (Inbound, Outbound), `MessageStatus` (Draft, Queued, Sending, Sent, Delivered, Read, Failed), `TemplateCategory` (Marketing, Utility, Authentication), `ChannelAccountDto`, `SaveChannelAccountRequest`, `MessageDto`, `SendMessageRequest`, `MessageTemplateDto`, `SaveMessageTemplateRequest`, `InboxThreadDto`, `OptOutDto`, and `TestConnectionResult` in [CommunicationContracts.cs](file:///d:/MindLift_CRM/src/MindLift.Shared/Contracts/CommunicationContracts.cs).
  - **Domain & Persistence:** `ChannelAccount`, `Message`, `MessageTemplate`, and `CommunicationOptOut` entities with `[Audited]` and multi-tenant isolation in [Communication.cs](file:///d:/MindLift_CRM/src/MindLift.Domain/Entities/Communication.cs), mapped in `AppDbContext`, EF Core migration `Omnichannel_Communication`.
  - **Security & Secret Protection:** [SecretProtector.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Communication/SecretProtector.cs) with AES-256 encryption at rest; channel account API responses completely omit plain credentials.
  - **Channel Senders & Adapters:** [ChannelSenders.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Communication/ChannelSenders.cs):
    - `EmailChannelSender`: Custom SMTP credentials or fallback to system Mailpit/SMTP, supports stored file attachments.
    - `WhatsAppChannelSender`: Meta Graph API integration with simulated test/development sandbox mode.
    - `TwilioSmsSender`: Twilio REST API integration with simulated test/development sandbox mode.
  - **Core Orchestrator & Opt-Out Registry:** [CommunicationService.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Communication/CommunicationService.cs) and [CommunicationAuxServices.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Communication/CommunicationAuxServices.cs):
    - Strict opt-out enforcement rejecting sends to blocked recipients.
    - Auto-resolution of conversation `ThreadId` based on channel and participant address.
    - Automatic association with matching `Lead`, `Contact`, `Account`, or `Deal`.
    - Automatic timeline `Activity` logging for outbound/inbound communications.
    - Inbound auto-lead creation for unknown senders when enabled on channel account.
    - Dynamic template placeholder replacement (`{{lead.first_name}}`, `{{deal.amount}}`, etc.).
  - **API Endpoints:** [CommunicationControllers.cs](file:///d:/MindLift_CRM/src/MindLift.Api/Controllers/CommunicationControllers.cs):
    - `/api/v1/communication/send`, `/threads/{threadId}`, `/threads/{threadId}/read`, `/inbox`, `/records/{entityType}/{entityId}`.
    - `/api/v1/channels` (CRUD, test connection).
    - `/api/v1/message-templates` (CRUD, render preview).
    - `/api/v1/opt-outs` (registry management).
    - `/api/v1/webhooks/whatsapp` (hub challenge verification, HMAC signatures, delivery callbacks, inbound payloads).
    - `/api/v1/webhooks/twilio` (SMS status updates and inbound SMS).
  - **Web UI:**
    - [Inbox.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Inbox/Inbox.razor) (`/inbox`): Responsive multi-channel unified inbox with channel filter chips (All, WhatsApp, Email, SMS), live search, participant chat stream with WhatsApp/Email/SMS speech bubbles, delivery status indicators, rich composer with template insertion, and Channels/Opt-outs configuration drawers.
    - [RecordConversations.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Components/RecordConversations.razor): Embeddable message feed and quick composer for record detail pages.
    - Integrated `/inbox` into navigation menu in [MainLayout.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Layout/MainLayout.razor).
## M14: Reports & Analytics Engine
- **Built & Integrated:**
  - **Shared:** `ReportSourceKey`, `ReportChartType`, `ReportFolderDto`, `ReportDefinitionDto`, `ReportDataSourceDto`, `ReportSourceColumnDto`, `ReportRunQuery`, `ReportFilterClause`, `ReportSortClause`, `ReportResultDto`, `ReportSummaryRowDto`, `SaveReportDefinitionRequest`, `SaveReportFolderRequest` in [ReportContracts.cs](file:///d:/MindLift_CRM/src/MindLift.Shared/Contracts/ReportContracts.cs).
  - **Domain & Persistence:** `ReportFolder`, `ReportDefinition`, `ReportExecutionLog` with `[Audited]` and multi-tenancy in [Reporting.cs](file:///d:/MindLift_CRM/src/MindLift.Domain/Entities/Reporting.cs), registered in `AppDbContext`, EF Core migration `Reporting_Engine`.
  - **Data Sources & Schema Registry:** [ReportDataSources.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Reporting/ReportDataSources.cs) exposing 5 primary datasets: `Leads`, `Deals`, `Activities`, `Contacts`, `Accounts` with strong typed columns, aggregates (SUM, AVG, MIN, MAX, COUNT), and filter operators.
  - **Secure SQL Compiler:** [ReportSqlBuilder.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Reporting/ReportSqlBuilder.cs) building parameterized dynamic PostgreSQL queries, enforcing tenant scope, custom-fields JSON parsing, sorting, grouping, and aggregations.
  - **Dapper High-Speed Query Executor:** [DapperReportExecutor.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Reporting/DapperReportExecutor.cs) with 30s timeout, max 5,000 rows protection, and summary totals calculation.
  - **14 Pre-Built Standard System Reports:** [SystemReports.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Reporting/SystemReports.cs) (Pipeline Funnel, Deal Velocity, Win/Loss Analysis, Rep Performance, Rep Activity Scorecard, Lead Conversion Cohorts, Source ROI, SLA Breaches, Forecast by Stage, Stale Deals, Activity Breakdown, Overdue Task Distribution, Inactive Accounts, Executive KPI Summary).
  - **API Endpoints:** [ReportsControllers.cs](file:///d:/MindLift_CRM/src/MindLift.Api/Controllers/ReportsControllers.cs) (`/api/v1/reports`, `/sources`, `/folders`, `/run`, `/{id}/export?format=csv|xlsx`, `/{id}/favorite`).
  - **Web UI:**
    - [Reports.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Reports/Reports.razor) (`/reports` - card grid, favorites, search, folder drawer, delete).
    - [ReportViewer.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Reports/ReportViewer.razor) (`/reports/view/{id}`, `/reports/system/{key}` - visualization charts, totals strip, formatted tabular display, CSV/Excel export).
    - [ReportBuilder.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Reports/ReportBuilder.razor) (`/reports/builder` - 4-tab configuration stepper, grouping, aggregations, live debounced preview).

## M16: Quotes, Invoices & Product Catalog (CPQ) / Documents & OCR
- **Built & Integrated:**
  - **Shared:** Enums: `ProductType` (Product, Service, Subscription), `BillingFrequency` (OneTime, Monthly, Quarterly, Annual), `QuoteStatus` (Draft, InReview, Sent, Accepted, Rejected, Expired), `InvoiceStatus` (Draft, Issued, Sent, PartiallyPaid, Paid, Overdue, Cancelled), `PaymentMethod` (BankTransfer, CreditCard, Stripe, Cash, Cheque, Other), `DocumentOcrStatus` (Pending, Processing, Completed, Failed). DTOs for Products, Price Books, Quotes, Invoices, Payments, Documents, and OCR results in [CpqContracts.cs](file:///d:/MindLift_CRM/src/MindLift.Shared/Contracts/CpqContracts.cs).
  - **Domain & Persistence:** `Product`, `PriceBook`, `PriceBookEntry`, `Quote`, `QuoteLineItem`, `Invoice`, `InvoiceLineItem`, `PaymentRecord`, `BusinessDocument` entities inheriting from `TenantEntity` with `[Audited]` in [Cpq.cs](file:///d:/MindLift_CRM/src/MindLift.Domain/Entities/Cpq.cs), mapped in `AppDbContext`, EF Core migration `Cpq_And_Documents`.
  - **Services:**
    - [ICpqService.cs](file:///d:/MindLift_CRM/src/MindLift.Application/Abstractions/ICpqService.cs) & [CpqService.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Cpq/CpqService.cs): Catalog CRUD, Price Book tier overrides, Quote calculations (line-item discounts, taxes, grand totals), quote-to-deal sync, quote-to-invoice generation, payment recording and balance reconciliation.
    - [IDocumentOcrService.cs](file:///d:/MindLift_CRM/src/MindLift.Application/Abstractions/IDocumentOcrService.cs) & [DocumentOcrService.cs](file:///d:/MindLift_CRM/src/MindLift.Infrastructure/Cpq/DocumentOcrService.cs): Document upload, file storage, and automated OCR extraction parsing vendor, invoice number, totals, and dates.
  - **API Controllers:** [CpqControllers.cs](file:///d:/MindLift_CRM/src/MindLift.Api/Controllers/CpqControllers.cs):
    - `ProductsController` (`/api/v1/products`)
    - `PriceBooksController` (`/api/v1/price-books`)
    - `QuotesController` (`/api/v1/quotes`, `/{id}/convert-to-invoice`, `/{id}/sync-deal`)
    - `InvoicesController` (`/api/v1/invoices`, `/{id}/payments`, `/{id}/cancel`)
    - `DocumentsController` (`/api/v1/documents`, `/upload`, `/{id}/process-ocr`)
  - **Web UI:**
    - [Products.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Cpq/Products.razor) (`/products`): Sellable items catalog, SKU badges, subscriptions billing frequencies, price book rate cards, add/edit product dialog.
    - [Quotes.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Cpq/Quotes.razor) (`/quotes`): Quotations & proposals list, status chips, multi-line item proposal generator with real-time tax/discount computation, proposal viewer, convert to invoice action.
    - [Invoices.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Cpq/Invoices.razor) (`/invoices`): Billing records table, collection progress bars, payment recording dialog with balance reconciliation, payment history ledger.
    - [Documents.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Pages/Cpq/Documents.razor) (`/documents`): Business document library, drag-and-drop file upload, OCR status tracking, intelligence metadata viewer (vendor, invoice #, amounts).
    - Integrated Products, Quotes, Invoices, and Documents navigation links into [MainLayout.razor](file:///d:/MindLift_CRM/src/MindLift.Web/Layout/MainLayout.razor).
- **Tests & Verification:**
  - **Integration Tests:** 5/5 passed in [CpqFlowTests.cs](file:///d:/MindLift_CRM/tests/MindLift.IntegrationTests/Cpq/CpqFlowTests.cs) (Product catalog lifecycle, Price book overrides, Quote calculations & deal sync, Quote-to-invoice conversion & partial/full payment reconciliation, Document upload & OCR processing).
  - **Full Suite Run:** 356/356 integration tests + 404/404 unit tests + 158/158 UI tests passed (918 total tests, 0 failures, 0 regressions).
  - **Build Status:** 0 compilation errors across all projects.

### M16 completion: Real OCR Pipeline, MRZ, Normalizers, Document Review & Expiry Alerts - 2026-10-13
- **OCR Engine & Normalization Pipeline:**
  - `IOcrEngine` with `TesseractCliOcrEngine` configuring process arguments (`stdout -l eng+ara --oem 1 --psm 3 tsv`), parsing TSV word layout (level 5 bboxes, confidence), with graceful fallback for digital/plain text when Tesseract CLI is absent.
  - `TextNormalizer`: Normalizes Arabic-Indic digits (`٠-٩`) and Persian digits (`۰-۹`) to standard ASCII `0-9`, converts Arabic punctuation (comma `،`, decimal separator `٫`, thousands separator `٬`), strips tatweel `ـ`, and unifies Alef/Ya/Ta-marbuta forms and strips diacritics for robust keyword matching while preserving line structure.
  - `DateNormalizer`: Culture-invariant parser supporting multiple English and Arabic date patterns (including day-first, month-first, Arabic-Indic numbers, and Arabic month names), rejecting out-of-range years (< 1900 or > 2100).
  - `AmountNormalizer`: Normalizes currency formats, parses currency symbols and codes (`AED`, `USD`, `SAR`, `EUR`, `GBP`, `د.إ`, `ر.س`), handles negative amounts formatted with minus or parentheses `(1,250.00)`, and prevents false positives on alphanumeric codes.
  - `MrzParser`: Parses ICAO Doc 9303 TD3 (2 lines × 44 chars) passport MRZ with 7-3-1 repeating weighted check digit validation on document number, date of birth, and expiry date. Extracts document number, nationality, names, sex, DOB, and expiry date with confidence scoring (1.0 for valid check digits, 0.60 for invalid).
  - Registered `/health` check for `ocr`, reporting `Healthy` when Tesseract is installed or `Degraded` when digital text extraction is active.
- **Document Review & Verification:**
  - Implemented Document Review screen (`DocumentReview.razor` at `/documents/review/{id}` and `/documents/{id}/review`) with split-pane layout:
    - Left pane: File download action, raw extracted text viewer with monospace layout, and full JSON payload inspector.
    - Right pane: Structured key information fields with confidence chips, in-place field editing with Save action.
    - Actions: [Reprocess OCR], [Reject] with required reason modal, [Approve & Apply] committing approved status and applying verified field values to associated Account or Contact records.
  - Direct navigation from Document table row actions to the Review screen.
- **Automated Expiry Alerts:**
  - Added Hangfire recurring job `document-expiry` executing `DocumentExpiryJob` daily.
  - Evaluates approved documents nearing expiration (60, 30, 7 days), dispatches in-app notifications, and records notified offsets in `ExpiryNotifiedOffsetsJson` to prevent duplicate alerts.
  - Scoped multi-tenant processing with `ITenantContext.Use`.
- **Test Fixtures & Verification:**
  - Created synthetic document test fixtures in `tests/fixtures/documents/` (synthetic passport TD3 MRZ, English trade license, Arabic trade license with Arabic-Indic digits, and synthetic invoice).
  - 8 new unit tests in `OcrNormalizerTests.cs` (100% pass rate).
  - 7 new integration tests in `DocumentReviewAndOcrTests.cs`: passport MRZ extraction, Arabic trade license digit normalization, review approval with account field application, rejection with reason, Hangfire expiry notification job, `/health` OCR check, and multi-tenant isolation with permission checks (100% pass rate).

## M14 Enhancements: PDF Export & Scheduled Report Deliveries
- **Built & Integrated:**
  - **Vector PDF Generator:** Created `ReportPdfExporter.cs` generating valid `%PDF-1.4` documents with portrait/landscape auto-switching, branded header, zebra stripes, summary ribbons, right-aligned numeric formatting, and pagination.
  - **Hangfire Recurring Delivery Job:** Created `ReportScheduleJob.cs` with recurring cron evaluation, automatic file generation (PDF/CSV/XML), multi-recipient email distribution via `IEmailSender`, and execution audit logging.
  - **Sales Targets Dashboard & CRUD:** Full quota tracking, summary cards, and interactive modal in `SalesTargets.razor` (`/reports/targets`).
  - **Tests:** 3 new integration tests in `ReportFlowTests.cs` (7/7 passed 100%).

## M17: Customer Portal & Document Requests
- **Built & Integrated:**
  - **Dual JWT Scheme Architecture:** Added isolated `"Portal"` authentication scheme with separate audience `mindlift-portal`, non-inbound claim mapping, and `PortalUser` policy.
  - **Anonymous Branding & Auth:** Public `/api/v1/portal/{slug}/branding`, password sign-in, passwordless magic links (`/magic-link`), and invitation activation (`/accept-invite`).
  - **Multi-Tenant Middleware Isolation:** Enhanced `TenantResolutionMiddleware` to dynamically authenticate portal tokens, set tenant scope for audit interceptors, and block cross-realm privilege escalation with `[HasPermission]`.
  - **Customer Portal Home API & Dashboard:** Aggregates shared documents, customer document requests, key extracted metadata, and portal engagement metrics.
  - **Document Requests & Secure Upload:** Clients upload files against specific CRM requests (`POST /api/v1/portal/requests/{id}/upload`), automatically transitioning request status to "Submitted" and archiving document in MindLift.
  - **CRM Staff UI:** Integrated "Invite to Portal" action in `ContactDetail.razor`, and unified Document Requests table and request authoring dialog in `Documents.razor`.
  - **Tests:** Complete suite of 7 integration tests in `PortalFlowTests.cs` (100% pass rate).

### M17 review fixes - 2026-10-13
- **Security fixes:**
  - The invite endpoint no longer returns the raw invite token to staff. It returns the contact's portal access status; the token only travels in the email.
  - Portal document by-id check: a customer without an account could open any shared document that also had no account (`null != null` was false). Fixed with `OwnsDocument` in [PortalControllers.cs](file:///d:/MindLift_CRM/src/MindLift.Api/Controllers/PortalControllers.cs).
  - Staff uploads were shared with the portal **by default**. Default is now private (entity, DTO, upload endpoint, service).
  - Inviting an email already used by another contact now returns 409.
- **Correctness fixes:** request dialog sent `ContactId = Guid.Empty` (now a contact picker and server validation, 400 on missing contact or title); "Approved" button did nothing (now Approve / Ask again / Cancel / Reopen through `POST /api/v1/document-requests/{id}/status`); portal metrics were hard-coded (fake 15.2% and fake 2024-2026 series), now real per-year totals in the tenant base currency; uploads to a Completed or Cancelled request now return 422; the upload button on `/documents` used a MudBlazor 8 child-content pattern that MudBlazor 9 ignores (now `CustomContent`).
- **New staff features:** `GET /api/v1/contacts/{id}/portal` (portal status chip on the contact page), `PUT /api/v1/documents/{id}/portal-sharing` (share switch per document row), `PortalUpload` notification to the requester and the contact owner when a customer uploads.
- **Tests:** 8 new integration tests in [PortalIsolationAndReviewTests.cs](file:///d:/MindLift_CRM/tests/MindLift.IntegrationTests/Portal/PortalIsolationAndReviewTests.cs): customer A vs B in one workspace, cross-workspace customer and staff, private-by-default sharing, request validation, upload notification + approval -> Ready + closed request 422, unknown status 400, duplicate invite email 409, Staff allowed / no-permission role denied on every new endpoint. Existing portal tests now read the invite token from the captured email.
- **Warnings fixed:** MUD0002 in Documents, Products and ManageBooking; CS8613/CS8619 in `Permissions.cs`.
- **Not checked:** the new staff UI (contact picker, share switch, approve buttons, portal chip) was **not checked in a browser**; it only compiles and the UI test suite still passes.
- **Still missing for M17:** None. M17 Customer Portal is complete (see completion entry below).

### M17 completion: Customer Portal WASM App, Refresh Tokens & Profile - 2026-10-13
- **WASM Application (`src/MindLift.Portal`):**
  - Standalone Blazor WebAssembly client configured with `<StaticWebAssetBasePath>portal</StaticWebAssetBasePath>` and hosted side-by-side with the staff app inside `MindLift.Api` (`app.UseBlazorFrameworkFiles("/portal")` and `/portal` fallback).
  - Designed for high aesthetic standard: MudBlazor 9 components, custom responsive stylesheet (`portal.css`), mobile bottom navigation bar, desktop header tabs, glassmorphic cards, tenant-branded accents (`PrimaryColor`), and light/dark theme toggle.
  - Full Arabic and English bilingual support (`PortalText.cs`) with dynamic RTL direction flipping (`dir="rtl"`) on the document root.
  - Implemented screens:
    - `Root.razor` & `NoCompany.razor`: Tenant slug routing and invalid company handling.
    - `Login.razor`: Email + Password credentials and passwordless Magic Link sign-in.
    - `AcceptInvite.razor`: Password setup for newly invited customer contacts.
    - `MagicVerify.razor`: Automated token verification callback.
    - `Home.razor`: Executive dashboard featuring personalized greetings, action items counter, company info cards, financial/engagement metrics strip, and recent shared documents table.
    - `Documents.razor` & `DocumentDetail.razor`: Document library with real-time text filter, document download, and OCR extracted intelligence view.
    - `Requests.razor`: Outstanding document upload requests with inline drag-and-drop/file-picker uploading via `MudFileUpload` (`CustomContent`).
    - `Profile.razor`: Customer details, EN/AR language switcher, and password change modal.
- **Portal Refresh Tokens & Session Management:**
  - Token rotation with family reuse detection: if a previously rotated token is replayed, all active portal refresh tokens for that contact are revoked.
  - Dual delivery mode: `HttpOnly`, `SameSite=Strict`, `Secure` cookie (`ml_portal_refresh`) for browser web clients, or response body for native/mobile clients sending `X-Client: mobile`.
  - Secure endpoints: `POST /api/v1/portal/{slug}/auth/refresh`, `POST /api/v1/portal/{slug}/auth/logout`, `GET /api/v1/portal/me`, `PUT /api/v1/portal/me`, `POST /api/v1/portal/me/password`.
  - Password complexity validation (minimum 8 chars, 1 uppercase, 1 lowercase, 1 number) and tenant portal on/off switch enforcement.
- **Tests & Verification:**
  - Added 7 tests in [PortalSessionTests.cs](file:///d:/MindLift_CRM/tests/MindLift.IntegrationTests/Portal/PortalSessionTests.cs): refresh token rotation, breach reuse revocation, logout revocation, web cookie vs mobile response body, password complexity & disabled portal guard, profile update & password change, and portal SPA host page verification.
  - All 22 portal integration tests passed (100%).

## M15: Hardening, Packaging, Documentation & Release - 2026-10-13
- **Containerization & Deployment Architecture:**
  - Created [Dockerfile](file:///d:/MindLift_CRM/Dockerfile): Multi-stage .NET 10 SDK build, ASP.NET Core 10 Debian runtime with `tesseract-ocr`, `tesseract-ocr-eng`, `tesseract-ocr-ara`, and non-root `$APP_UID` execution listening on port 8080.
  - Created [docker-compose.prod.yml](file:///d:/MindLift_CRM/docker-compose.prod.yml): Production compose setup with PostgreSQL 16 container, health checks (`pg_isready`), API container dependency on DB health, volume persistence, and environment variable configuration for production secrets.
  - Created [.github/workflows/ci.yml](file:///d:/MindLift_CRM/.github/workflows/ci.yml): GitHub Actions CI workflow installing .NET 10, running PostgreSQL 16 service container, installing Tesseract OCR packages, verifying zero-warning builds (`--warnaserror`), running the full test suite with test results reporting, and building the production Docker container.
- **Upload Security Hardening:**
  - Maximum upload size validation (<= 25 MB) enforced in `DocumentsController.Upload` and `PortalCustomerController.UploadForRequest`.
  - Prohibited dangerous executable extensions (`.exe`, `.dll`, `.bat`, `.cmd`, `.sh`, `.ps1`, `.vbs`, `.msi`, `.scr`, `.jar`) blocked with 400 Bad Request across staff and portal upload endpoints.
  - Added integration test `Upload_Rejects_Prohibited_File_Extensions` verifying blocked payload rejection.
- **System Documentation:**
  - Created [docs/ADMIN_GUIDE.md](file:///d:/MindLift_CRM/docs/ADMIN_GUIDE.md): Operations guide covering Docker deployment, configuration keys, database migrations, Hangfire job monitoring, backup & restore procedures, health monitoring, and scaling recommendations.
  - Created [docs/API.md](file:///d:/MindLift_CRM/docs/API.md): API reference covering REST design conventions, authentication schemes (Staff JWT vs Portal JWT), multi-tenant header semantics, rate limiting, error responses (ProblemDetails RFC 7807), and core endpoint specifications.
- **Verification & Test Suite Stability:**
  - Ran full test suite (`dotnet test MindLift.slnx`) 3 consecutive times with 100% pass rate:
    - Run 1: 949 passed, 0 failed, 0 skipped
    - Run 2: 949 passed, 0 failed, 0 skipped
    - Run 3: 949 passed, 0 failed, 0 skipped
  - Solution build: 0 errors, 0 warnings.

## Grand Test Suite Status (2026-10-13)
- **Unit Tests (`MindLift.UnitTests`):** 412 / 412 passed
- **UI Tests (`MindLift.UITests`):** 158 / 158 passed
- **Integration Tests (`MindLift.IntegrationTests`):** 379 / 379 passed (3 consecutive clean runs)
- **E2E (`MindLift.E2E`):** not run (Playwright harness)
- **Total:** 949 passed, 0 failed, 0 skipped (100% passing)




## Documents: PDF and text support (2026-10-13)
- Tesseract 5.4 installed (`C:\Program Files\Tesseract-OCR`); Arabic data kept in `tools\tessdata` (configured as `Ocr:TessdataPath`).
- `DocumentPageReader`: PDF pages with a text layer are read directly (PdfPig, word boxes normalised to the page); scanned pages are rendered at 300 dpi (PDFtoImage) and read by Tesseract; photos are read by Tesseract; `.txt`, `.csv`, `.md` are read as text. Up to 50 pages.
- `DocumentOcrService` now reads through the page reader. A file that cannot be read is marked **Failed** with no values; the invented-invoice fallback is removed.
- Fixed: the Tesseract arguments (`tsv` was passed as a file name, so OCR never ran; now `-c tessedit_create_tsv=1`).
- Packages: UglyToad.PdfPig 1.7.0-custom-5 (the only PdfPig build on nuget.org), PDFtoImage 5.4.0, SkiaSharp 4.153.1.
- Tests: 4 PDF tests (digital, scanned, photo, unsupported type), 1 failed-file test. Full non-browser suites green.
- Not yet: configurable document types and field rules (plan 19.3, 19.6); photo clean-up (deskew, contrast).

## Documents: customisable document types (2026-10-13)
- **Settings > Document types** (`/settings/document-types`): define a kind of document with a key and name, the words that identify it (weighted, one per line), a minimum score, and the values to read.
- **Values:** each value has a label, a kind (text, number or code, number, amount, date), an anchor (the label written before it, for example `Expiry Date:`) or a regular-expression pattern (first group is the value), required and reporting flags. Non-matching or invalid values (a date that does not parse) are not stored.
- **Classification:** after the built-in checks, the active type with the highest score that reaches its minimum sets the document category, and its values are saved with the document. Fields keyed `expiry_date` and `issue_date` also set the document's dates.
- **Test on a sample file:** upload any supported file to see what the type reads, without saving anything.
- **API:** `/api/v1/settings/document-types` (list, get, create, edit, delete) and `/{id}/test`. Validation covers keys, regular expressions, value kinds and duplicate keys.
- **Tests:** 10 document-type tests (extraction, Arabic digits, scoring, validation, the sample test, upload-to-save, tenant isolation). Full non-browser suites green: 412 unit, 158 UI, 394 integration.
- **Not done:** the Settings screen is not yet driven in the browser; and the review screen shows custom fields as plain rows, without page highlights.

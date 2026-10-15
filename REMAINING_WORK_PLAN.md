# MindLift CRM: Remaining Work Plan

**Date:** 2026-10-09
**Read with:** `CRM_BUILD_PLAN.md` (the specification) and `PROGRESS.md` (what is done).
**Purpose:** This file lists everything that still has to be built and tested, in the order to do it. Section numbers such as "7.3" or "18.15" refer to `CRM_BUILD_PLAN.md`.

---

## 1. Where the project stands

| Milestone | Backend | Web screens | Tests | Status |
|---|---|---|---|---|
| M0 Environment | Done | Done | Done | Complete |
| M1 Persistence and core | Done, except file endpoints | None | Done | File upload and download missing |
| M2 Auth, tenants, roles | Done | Done | Done | Complete |
| M3 Settings and fields | Done | Done | Done | Complete |
| M4 Onboarding and trial | Done | Done | Done | Complete |
| M5 Design system and shell | Done | Most done | Manual only | Shared components and E2E harness missing |
| M6 Leads | Done | Most done | 10 integration tests plus manual | Kanban, import, export, merge screens missing |
| M7 to M17 | Not started | Not started | Not started | Open |

**Last results:** 47 unit tests passed, 74 integration tests passed. Some integration tests return HTTP 500 at random during full runs (see 2.3).

**How to run locally:** Docker Desktop must be running (Postgres on host port 5433, Mailpit on 8025). Then:

```bash
dotnet run --project src/MindLift.Api --launch-profile http
```

The app opens at `http://localhost:5082`. Local test login: `owner.test@mindlift.localhost` / `Test-Passw0rd-2026` (a synthetic account on this machine only).

---

## 2. Carry-over work from M0 to M6 (do first)

Finish these before M7. Later milestones depend on them.

### 2.1 Build

| # | Item | From | Notes |
|---|---|---|---|
| C1 | File upload and download endpoints: `POST /api/v1/files`, `GET /api/v1/files/{id}` with record permission checks | M1 | `IFileStorage`, `LocalFileStorage` and the `files` table already exist. M9, M12 and M16 need this |
| C2 | Shared components: `FileDropZone`, `ChartCard`, `ConfirmDialog` | M5 (18.7) | `ChartCard` is needed by M13. `FileDropZone` is needed by M6 import and M16 |
| C3 (DONE) | Generic components: `EntityListPage` (18.8.1), `FilterBuilder`, `DynamicForm` (18.8.4), `FieldValue`, `RecordDetailLayout` (18.8.2), `Timeline`, user, record and date pickers | M5 | Build these **before** M7 so contacts, accounts and deals reuse them instead of copying the leads pages |
| C4 | Refactor the lead list and detail pages onto `EntityListPage` and `RecordDetailLayout` | M6 | Proves the generic components work |
| C5 | Leads kanban board by status | M6 (step 8) | |
| C6 | Lead import wizard (upload CSV, map columns, preview, run, result) and export button | M6 (steps 7 and 8) | API exists. Make the pipeline generic (4.10) so M7 and M8 reuse it |
| C7 | Merge action on lead screens: pick duplicates, choose survivor, confirm | M6 | API exists: `POST /api/v1/leads/{id}/merge` |
| C8 | Disqualify form: highlight the empty Reason field, not just a toast | M6 | |
| C9 (DONE 2026-10-15, native review pending) | Arabic strings: move all UI text into `.resx` files, translate, and mirror the pager arrows in RTL | M5 | Staff app text now goes through `Translator`, with 1,500+ Arabic phrases in `src/MindLift.UI/Localization/Arabic.resx`; the language switch changes text and layout; page arrows mirror. Not done: custom Arabic stage names on the deal board and list (needs an API change), server-generated text (validation messages, notification and email bodies), and a native-speaker review of the wording. |
| C10 | Tenant brand colour applied to the theme | M5 | `PrimaryColor` is already saved in company settings |
| C11 | Route gating: redirect to the onboarding wizard until setup is finished | M5 | |
| C12 | Notifications bell (static for now) and global search box (wired in M7) | M5 (step 9) | |
| C13 | Hide `/_styleguide` in production builds | M5 | |
| C14 | Refresh-token cleanup job | M2 | Can wait for M15's `CleanupJob` |

### 2.2 Test

| # | Item | Notes |
|---|---|---|
| T1 | Create `tests/MindLift.E2E` with Playwright, the `seed-demo` command and the screenshot harness (18.16) | Required by every milestone with screens. Do this first |
| T2 | E2E tests for sign-up, onboarding wizard, every settings page, lead list, lead detail, convert, disqualify, edit | Today these were checked by hand only |
| T3 | Screenshots of every M5 and M6 screen at 1440, 768 and 375 wide in EN and AR (18.15 UI DoD) | |
| T4 | Assignment rule with **two or more users**: round robin alternates, fewest-open-leads picks the lighter user, a non-matching rule falls through to the workspace owner | Today's check could not prove the rule ran |
| T5 (DONE) | Web form drawer and assignment rule drawer driven in the browser (create, edit, toggle fields) | Only their API calls were checked |
| T6 | Public web form: honeypot rejects bots, inactive form rejects submissions, duplicate submission is flagged | |
| T7 (DONE except the layout save screen) | Settings pages: team, branch, stage create, edit and delete; layout save; role matrix save for a non-admin role | Pages render; the write paths were not all exercised |
| T8 (DONE) | Live email through Mailpit (invitation, password reset, verification) | Tests use a recording sender so far |

### 2.3 Fix

| # | Issue | How to approach it |
|---|---|---|
| F1 | Integration tests sometimes return HTTP 500 in a full run, then pass on the next run | Run the suite 3 times in a row. Capture the first server exception of each failing test. Likely a race between the first request and migration on a new per-host database. Fix by migrating before the host serves requests |
| F2 | Blank page after a rebuild: the browser keeps an old Blazor bootstrap file | Set no-cache headers on `index.html` and the `_framework` boot files in Development. Always stop the API before building, then build `MindLift.Web` and `MindLift.Api` |

---

## 3. Milestones still to build

Build in this order (plan section 14): **M7 → M8 → M9 → M10 → M11 → M12 → M13 → M14 → M16 → M17 → M15**. M15 is always last.

Every milestone with screens must pass the UI DoD (18.15) with screenshots (18.16) before it is called done.

### M7: Contacts and Accounts (4 days)

**Build**
- Contact and account CRUD with custom fields, data scope and timeline
- Contact duplicate detection and merge; account hierarchy (parent account)
- 360° summary on contact and account (deals, activities, history)
- Import and export for both, reusing the generic pipeline from C6
- Global search endpoint and the typeahead in the top bar
- List and detail pages with related tabs, built on the C3 components

**Test**
- Integration: CRUD, data scope (Own, Team, All), tenant isolation, merge, hierarchy loops rejected
- Converted lead's history shows on the new contact (7.3)
- Account page totals match its contacts, deals and activities
- Search finds records across modules and respects permissions
- E2E plus screenshots for list and detail

**Done when:** a converted lead's history is visible on the contact, and the account page aggregates contacts, deals and activities.

### M8: Deals and Pipeline (5 days)

**Build**
- Deal CRUD with currency conversion, probability and several contacts
- Stage change logic, stage history, rotting (stale) flags, board endpoint
- Customer onboarding checklist templates, triggered when a deal is Won (7.4), and the Onboarding tab
- Import and export
- Kanban board with drag and drop, Won and Lost dialogs, list view, detail page with stage bar and stage history

**Test**
- Unit: currency conversion with dated exchange rates; probability per stage
- Integration: stage history written on every move; a lost reason is required; Won creates the onboarding checklist; rotting days flag stale deals
- E2E: drag a deal across stages, win it, lose it
- Acceptance criteria 7.7

### M9: Activities and Timeline (4 days)

**Build**
- Task, call, meeting and note CRUD; linking and auto-fill; complete, reopen, follow-up
- Keep `LastActivityAt` and `NextActivityAt` up to date on records
- Mentions with HTML sanitization
- Timeline endpoint for every entity
- `.ics` invites for meetings by email
- Activity composer on every record page; My Activities page

**Test**
- Sanitizer blocks script and unsafe HTML
- Every action on a record appears on its timeline (manual checklist across all modules)
- `.ics` file arrives in Mailpit and opens in a calendar app

### M10: Notifications and reminders (3 days)

**Build**
- `INotificationService`, user preferences, SignalR delivery, grouped email delivery job
- Hook every event in 7.11 (assignment, mention, deal won, import finished and so on)
- Reminder jobs: activity due, overdue, daily digest
- Bell dropdown, notifications page, preferences page, live toasts

**Test**
- Assigning a lead to another user shows them a toast within 5 seconds
- An overdue task notifies exactly once
- Preferences turn email or in-app delivery off

### M11: Calendar and booking (5 days; M11b sync can wait)

**Build**
- Calendar events endpoint and reschedule
- Booking page settings, availability service, public booking pages and API, manage booking (cancel, reschedule), confirmations and reminders
- Calendar UI (day, week, month; user and team filters), booking settings, public booking pages in EN and AR
- M11b (deferred): Google and Microsoft OAuth, two-way sync, busy slots

**Test**
- Unit: availability across time zones, buffers, working hours and existing meetings
- A customer books a slot: the meeting lands on the user's calendar, the lead or contact is linked, confirmation arrives in Mailpit, the slot is no longer offered
- Two people cannot book the same slot at the same moment

### M12: Communication (6 days)

**Build**
- Channel account settings with encrypted secrets and a "test connection" button
- Email: SMTP send, IMAP poll, threading, matching to records, attachments
- WhatsApp Cloud API: text, template and media; webhook verification and signature check; delivery statuses; 24-hour rule; template sync
- SMS through Twilio with status webhook
- Message templates with preview; opt-outs
- Optional auto-create of leads from inbound messages
- Composer, conversation tabs on records, Inbox page
- Fake providers for development and tests

**Test**
- Email sent from a contact appears in Mailpit and on the timeline; a reply threads correctly
- WhatsApp flow through the fake provider; webhook with a bad signature is rejected
- Opted-out contacts cannot be messaged
- Secrets are never returned by the API

### M13: Dashboard (5 days, priority feature)

**Build**
- `SalesMetrics` and `TargetMetricCalculator` (8.6)
- Sales targets entity and CRUD API
- Widget infrastructure, `DashboardService`, caching, saved layouts
- All Phase 1 widgets (8.4), KPI tiles (8.5), drill-down links
- SignalR `DashboardStale` refresh
- Dashboard page with customize mode, filter bar, view switcher (Staff, Manager, Admin), responsive grid

**Test**
- Unit: every metric against known sample data
- Each widget respects data scope (a staff user sees only their numbers)
- Drill-down list totals equal the widget number
- Load time meets the target in 8.8 on the performance seed
- Acceptance criteria 8.8

### M14: Reports and Analytics (8 days, priority feature)

**Build**
- Data sources (9.2) for leads, contacts, accounts, deals, activities and stage history, including custom fields
- `ReportSqlBuilder`, `FilterSqlCompiler`, `DapperReportExecutor`, formulas and totals
- System reports 1 to 13 (9.6)
- Report definitions, folders, favourites and sharing API (9.8)
- Excel, PDF and CSV export (9.9)
- Scheduled reports by email with a run log (9.10)
- Targets grid (9.11)
- Reports home, viewer (parameters, chart, table, drill-down) and builder with live preview

**Test**
- Unit: every filter operator and formula, SQL injection attempts through filters and field names
- Each system report gives the expected result on fixed seed data
- Exports open correctly in Excel and a PDF viewer
- A scheduled report arrives in Mailpit on time
- Acceptance criteria 9.14

### M16: Documents and Document Intelligence, OCR (8 days)

**Build**
- Install Tesseract (19.4); `Ocr` config; `/health` reports the OCR engine and languages
- Document entities, migrations and upload endpoints (needs C1)
- Pipeline (19.5): PDF text layer, page rendering, image cleanup, Tesseract, layout model, classification, extraction, save
- Extraction engine (19.6): value patterns, anchor and regex rules, MRZ parser, date and amount normalizers (including Arabic-Indic digits), confidence, auto-approve
- Built-in document types (19.7)
- Review and approve flow (19.9), "Apply to record", expiry dates with notifications, full-text search on OCR text
- Document metrics: report data source, reports 14 and 15, dashboard widgets, Documents tabs on contact, account and deal
- Documents list, upload drawer, **document review screen** (18.9.12), document type settings with a test playground

**Test**
- Unit: every value pattern, rule kind, MRZ case and normalizer
- Integration with synthetic fixtures in `tests/fixtures/documents/`: one digital PDF, one scanned PDF, one JPG, one Arabic document. **Use synthetic documents only, never real personal documents**
- Expiry job notifies once per document
- Acceptance criteria 19.12

### M17: Customer Portal v1 (6 days)

**Build**
- Portal users, invitations and magic links, separate JWT scheme and audience, refresh tokens
- Portal API with strict contact and account scoping; branding endpoint
- `MindLift.Portal` WASM app at `/portal` using `MindLift.UI` and tenant branding
- Pages: login and magic link, home, documents list and detail, requests (upload against a request), profile
- Staff side: portal access card on contact, "share with portal" toggle on documents, document requests, notifications when a customer uploads

**Test**
- Customer A cannot see customer B in the same tenant, nor anything in another tenant
- A staff token is rejected by the portal API, and a portal token by the staff API
- Upload, OCR, staff approval, then the value appears in the portal
- All portal screens pass the UI DoD in EN and AR at 375 wide
- Acceptance criteria 20.9

### M15: Hardening and release (5 days, always last)

**Build**
- Security checklist (section 12) fully ticked; optional TOTP 2FA
- Performance seed and index tuning until dashboard and report timings are met
- Accessibility, RTL and phone-width pass on every page
- Multi-stage Dockerfile, `docker-compose.prod.yml`, environment config, backup notes, health checks, log shipping
- GitHub Actions CI: restore, build, test with a Postgres service, publish the image
- `docs/ADMIN_GUIDE.md` and `docs/API.md`

**Test**
- Clean deploy from scratch on a fresh VM with docker compose
- Full regression suite green, run 3 times in a row with no random failures
- Penetration-style checks: tenant isolation, authorization on every endpoint, rate limits on login and public forms, file upload type and size limits

---

## 4. Rules for every milestone

1. **Plan, build, test, record.** Before coding, list the steps. After coding, run the unit and integration tests. Then add an entry to `PROGRESS.md` with what was built, what was verified, and what was not.
2. **Reuse, don't copy.** New record screens use `EntityListPage`, `RecordDetailLayout`, `DynamicForm` and `Timeline`.
3. **Tenant isolation test for every new entity.** Tenant A must never see tenant B's rows.
4. **Permission test for every new endpoint.** One allowed role, one denied role.
5. **UI DoD (18.15)** with screenshots at 1440, 768 and 375 wide in EN and AR.
6. **Synthetic data only** in tests and fixtures.
7. **Report honestly.** If something was not checked, write "not checked" in `PROGRESS.md`.
8. **Razor rule learned in M6:** don't put string literals inside an `@(...)` expression in a double-quoted attribute. Use a constant or a helper method instead. Alias `FilterOperator` and `FieldType` in pages that also use MudBlazor.

---

## 5. Suggested schedule

| Order | Work | Estimate |
|---|---|---|
| 1 | Carry-over: T1 (E2E harness), F1, F2, C1 to C4 | 4 days |
| 2 | Carry-over: C5 to C13, T2 to T8 | 4 days |
| 3 | M7 Contacts and Accounts | 4 days |
| 4 | M8 Deals and Pipeline | 5 days |
| 5 | M9 Activities and Timeline | 4 days |
| 6 | M10 Notifications | 3 days |
| 7 | M11 Calendar and booking | 5 days |
| 8 | M12 Communication | 6 days |
| 9 | M13 Dashboard | 5 days |
| 10 | M14 Reports | 8 days |
| 11 | M16 Documents and OCR | 8 days |
| 12 | M17 Customer Portal | 6 days |
| 13 | M15 Hardening and release | 5 days |
| | **Total** | **about 67 developer-days** |

If a demo is needed sooner, follow the plan's short path: carry-over, M7, M8, part of M13, M16, M17, then M14.

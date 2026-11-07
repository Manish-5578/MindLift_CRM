# MindLift Mobile App — Strict Delivery Rules

## 1. Scope and product goal

- Build a **staff-facing cross-platform mobile application** for MindLift CRM, targeting Android and iOS from one Flutter codebase.
- The mobile app is a field-work product, not a shrunken copy of the web app. Prioritize fast lead follow-up, activities, contacts, deals, calendar actions, offline work, and notifications.
- Do not build billing administration, report builders, system configuration, platform administration, or complex desktop-only screens in the first mobile release.
- Preserve MindLift’s multi-tenant, role-based, module-gated, Arabic/English product behavior. Never weaken backend authorization to simplify mobile work.

## 2. Mandatory technology choices

- Use Flutter and Dart. Keep the app in a new `mobile/` directory and use a feature-first package structure.
- Use a custom internal design-system package named `mindlift_ui`.
- Use Riverpod for application state, `go_router` for navigation, Dio for API requests, `flutter_secure_storage` for secrets, and Drift/SQLite for the offline store.
- Treat the existing ASP.NET API as the system of record. The mobile app must not access PostgreSQL, Redis, storage buckets, or third-party CRM providers directly.
- Reuse the existing mobile token mode (`X-Client: mobile`) and offline endpoints. Do not create duplicate authentication or sync protocols without an approved backend design.

## 3. No native UI systems

- Do **not** use Material widgets, Cupertino widgets, platform-styled component libraries, or default platform dialogs as visible product UI.
- Do not use `Scaffold`, `AppBar`, `NavigationBar`, `BottomNavigationBar`, `FloatingActionButton`, `AlertDialog`, `SnackBar`, Material buttons, Material text fields, or Cupertino equivalents for product screens.
- Build all visible controls from Flutter base primitives such as `Container`, `DecoratedBox`, `Text`, `Icon`, `GestureDetector`, `Focus`, `Semantics`, `CustomScrollView`, `Overlay`, `Animated*`, and `CustomPainter` where necessary.
- Platform APIs may be used only for invisible operating-system capabilities: secure key storage, biometrics, camera, file selection, maps, calling, push notifications, and permission prompts.
- Never imitate iOS or Android system chrome. MindLift UI must have one intentional visual language on both platforms.

## 4. Design-system rules

- Before building feature screens, create tokens for colour, typography, spacing, radii, borders, shadows, motion, touch targets, dark mode, and RTL layout.
- Create and test reusable primitives before feature work: `MlScreen`, `MlButton`, `MlIconButton`, `MlTextField`, `MlSelect`, `MlSheet`, `MlDialog`, `MlToast`, `MlRecordCard`, `MlStatusChip`, `MlAvatar`, `MlEmptyState`, `MlSkeleton`, `MlTimeline`, and `MlBottomNavigation`.
- Minimum touch target: 44 by 44 logical pixels. Support text scaling up to 200% without clipped content or inaccessible actions.
- All meaningful visual state must have a non-colour signal: text, icon, pattern, or semantic label.
- Use a bottom navigation with no more than five primary destinations: Home, Leads, Deals, Activity, and More.
- Put fast actions in a custom quick-add sheet: Lead, Task, Call, Note, and Meeting.
- Prefer agenda and timeline experiences over desktop-style tables. Use cards, progressive disclosure, and full-screen record details.
- Support English LTR and Arabic RTL from the first screen. Never mirror numbers, dates, money, phone numbers, map direction, or brand marks incorrectly.

## 5. Architecture rules

- Use a feature-first structure: `core/`, `design_system/`, `features/auth/`, `features/home/`, `features/leads/`, `features/contacts/`, `features/deals/`, `features/activities/`, `features/calendar/`, `features/notifications/`, and `features/offline/`.
- Each feature owns its presentation, application state, repository interface, API data source, local data source, models, and tests.
- UI widgets may not call Dio, read secure storage, parse API JSON, or contain business rules.
- Repositories own cache-versus-network decisions. API DTOs must not leak into presentation widgets.
- Use typed API clients and explicit DTO mapping. Reject silently ignored server fields and unknown enum values.
- Every mutation must expose loading, success, failure, retry, and offline-queued states.

## 6. Authentication and security

- Store access and refresh tokens only in secure platform storage. Never use shared preferences, plain SQLite, logs, analytics events, screenshots, or crash reports for tokens or PII.
- Send `X-Client: mobile` only through the central authenticated client.
- Implement single-flight token refresh: concurrent expired requests must wait for one refresh operation, then retry once only.
- On refresh-token reuse, session revocation, tenant suspension, or 401/403, clear secure storage, clear the offline database, terminate SignalR, and return to sign-in.
- Do not trust local role, module, feature-flag, or ownership state. Render it for UX only; the API remains authoritative.
- Require biometric re-authentication before changing password, viewing especially sensitive documents, or exporting/sharing files when the product policy enables it.
- Redact email addresses, telephone numbers, document values, bearer tokens, refresh tokens, request bodies, and URLs containing tokens from mobile logs.
- Never bypass TLS validation. Certificate pinning requires a documented rotation and incident plan before adoption.

## 7. Offline and sync rules

- Offline is mandatory for leads, contacts, deals, activities, notes, and task completion. Do not claim offline support for any feature that has no tested sync behavior.
- Encrypt the local database and keep only the user’s permitted working set; do not download an entire tenant by default.
- Use server-provided record versions and the existing `/api/v1/offline/bootstrap`, `/delta`, and `/sync` contract.
- Persist an append-only local outbox. Every mutation needs a client mutation ID, base version, timestamp, entity type, retry count, and human-readable action label.
- Never delete a queued mutation automatically. Show pending, syncing, failed, and conflict states.
- On conflict, preserve both local and server values, show a clear resolution screen, and require an explicit user choice unless a documented safe merge policy exists.
- Sync must be idempotent, retry with exponential backoff, pause on auth failure, and resume safely after app restart.
- File uploads need resumable/retry behavior or a clear “upload pending” state. Never falsely show a document as uploaded before the server confirms it.

## 8. Notifications and deep links

- SignalR is for foreground updates only. Background and terminated-state alerts require a backend device-registration model and FCM/APNs delivery.
- Do not add client push code until the backend has device registration, revocation, tenant/user binding, delivery failure handling, and notification preference enforcement.
- Every notification payload must contain only opaque IDs and an allowed deep-link target; do not put customer data, CRM notes, or secrets in push text or payload data.
- Deep links must re-authorize through the API before showing any record.
- Ask for push permission contextually after the user sees value, not immediately on first launch.

## 9. Module and workflow priorities

- Phase 1 only: sign-in, My Day, leads, contacts/accounts, activities, deal stage actions, notifications, offline queue, profile, language, and session management.
- Phase 2: document capture, camera/gallery upload, calendar agenda, booking actions, inbox/tickets, and compact dashboard cards.
- Phase 3: voice notes, map/check-in flows, call logging, QR scanning, customer portal app, and advanced mobile dashboards.
- A feature is complete only when its API permissions, empty/loading/error/offline states, analytics events, Arabic/RTL behavior, and tests are complete.

## 10. API contract rules

- Do not change a server route, request shape, response shape, authorization policy, or DTO without updating the OpenAPI contract, server tests, and mobile client generation/mapping.
- All list requests need pagination, bounded page size, search/filter behavior, and deterministic sorting.
- Use idempotency keys for mobile creates and payments. Never retry non-idempotent writes blindly.
- Date/time values cross the API as UTC ISO-8601; display them in the user or tenant timezone. Never use device locale as a data-storage format.
- Money must carry amount and ISO currency. Never infer a currency from device locale.

## 11. Quality gates

- No feature may be merged without unit tests for state/repository behavior and widget tests for its visible states.
- Every critical field workflow needs integration coverage: login/refresh, lead creation, task completion, offline sync, conflict resolution, notification deep link, document upload, and session revocation.
- Test Android and iOS on at least one physical device before a release candidate.
- Test airplane mode, intermittent network, slow network, app kill/restart during sync, expired refresh token, Arabic RTL, dark mode, and large text.
- Maintain an accessibility checklist: screen-reader labels, focus order, contrast, dynamic text, touch target size, and motion reduction.
- Run static analysis, formatting, dependency auditing, unit/widget tests, integration tests, and release builds in CI.

## 12. Agent behavior and safety

- Before editing, inspect relevant existing API contracts, permissions, offline behavior, tests, and current UI conventions.
- Write or update a short implementation plan before multi-file changes.
- Make the smallest coherent change. Do not refactor unrelated web or backend code while implementing a mobile task.
- Never run destructive commands, delete user data, modify production infrastructure, rotate secrets, or publish a build without explicit approval.
- Never invent test results. Report commands run, outcomes, and unverified assumptions.
- If requirements conflict with tenant isolation, authorization, token safety, or data privacy, stop and request a decision.

## 13. Definition of done

A mobile feature is done only when all apply:

1. It uses the custom MindLift design system and no native UI system.
2. It works in English LTR and Arabic RTL.
3. It has loading, empty, error, permission-denied, and offline states.
4. It respects API authorization and tenant isolation.
5. Its mutations are safe, observable, and recoverable.
6. It has automated tests at the appropriate level.
7. It has been tested on both Android and iOS where platform behavior differs.
8. Its analytics and crash-reporting behavior does not leak sensitive data.

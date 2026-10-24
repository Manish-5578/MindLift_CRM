# MindLift CRM — Administrator & Operations Guide

This guide details the deployment, configuration, maintenance, and operational management of the MindLift CRM platform.

---

## 1. System Requirements & Architecture

### Technology Stack
- **Backend Runtime:** .NET 10 SDK / ASP.NET Core
- **Database:** PostgreSQL 16+ with `pgcrypto`
- **Background Jobs:** Hangfire 1.8+ backed by PostgreSQL storage
- **Frontend SPA (Staff):** Blazor WebAssembly (.NET 10) hosted at `/`
- **Frontend SPA (Portal):** Blazor WebAssembly (.NET 10) hosted at `/portal`
- **OCR Engine:** Tesseract 5 (with English `eng` and Arabic `ara` traineddata)
- **Containerization:** Docker & Docker Compose

### Architectural Overview
MindLift CRM operates as a high-density, multi-tenant software-as-a-service application. Data isolation is maintained via tenant identifiers on all database records, enforced at compile-time and runtime using global query filters, multi-tenant interceptors, and strict claim-based security policies.

```
                           [ Internet ]
                                │
                        [ Reverse Proxy ]
                                │
                     ┌──────────┴──────────┐
                     ▼                     ▼
               / (Staff SPA)        /portal (Customer SPA)
                     │                     │
                     └──────────┬──────────┘
                                ▼
                        [ MindLift.Api ]
                     (:8080 / ASP.NET Core)
                      │        │         │
          ┌───────────┘        │         └───────────┐
          ▼                    ▼                     ▼
    [ PostgreSQL 16 ]    [ Hangfire Server ]   [ Tesseract OCR ]
    (Tenants & Data)     (Queues & Jobs)       (Doc Intelligence)
```

---

## 2. Production Deployment via Docker

### Quick Start
To launch the production stack with PostgreSQL, Hangfire workers, and the web/API containers:

1. Clone the repository and navigate to the project directory:
   ```bash
   git clone https://github.com/your-org/MindLift_CRM.git
   cd MindLift_CRM
   ```

2. Create a production environment file (`.env`):
   ```env
   POSTGRES_DB=mindlift
   POSTGRES_USER=mindlift_admin
   POSTGRES_PASSWORD=UseAStrongRandomPassword123!
   JWT_SIGNING_KEY=<a random string of at least 32 characters>
   SECRET_KEY=<a different random string; it encrypts stored mail passwords and provider tokens>
   APP_BASE_URL=https://crm.example.com
   ```

3. Launch with Docker Compose:
   ```bash
   docker compose -f docker-compose.prod.yml up -d
   ```

4. Verify service health:
   ```bash
   docker compose -f docker-compose.prod.yml ps
   curl -f http://localhost:8080/health
   ```

---

## 3. Environment Variables Reference

| Variable | Required | Default | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | Hosting environment (`Development` or `Production`). |
| `ConnectionStrings__Default` | **Yes** | — | PostgreSQL connection string with host, port, database, user, and password. |
| `Database__MigrateOnStartup` | No | `true` | When true, applies pending EF Core migrations on application boot. |
| `Jwt__SigningKey` | **Yes** | — | HMAC-SHA256 secret key for signing staff access tokens (min 32 bytes). |
| `Jwt__Issuer` | No | `https://app.mindlift.io` | Staff JWT issuer URL. |
| `Jwt__Audience` | No | `mindlift-api` | Staff JWT audience string. |
| `Security__SecretKey` | **Yes** | falls back to the signing key | Encrypts the mail passwords, provider tokens and webhook secrets a workspace stores. Changing it makes values saved with the old key unreadable, so those secrets must be entered again. |
| `App__BaseUrl` | **Yes** | `http://localhost:5082` | The public address of the app, for example `https://crm.example.com`. It is put into emailed links (sign-in, invitations, booking and reminder emails). Without it, links point at localhost. |
| `Communication__WhatsAppVerifyToken` | If WhatsApp is used | — | The token you give Meta when you register the WhatsApp webhook. If it is not set, the registration handshake is refused. |
| `Communication__PublicBaseUrl` | No | `App__BaseUrl` | The address Twilio is configured to call. Twilio signs this exact address, so set it if a proxy changes the address the app sees. |
| `RateLimiting__PublicPermitsPerMinute` | No | `120` | Requests per minute per client address for sign-in, sign-up, magic links, public booking and website forms. Over the limit gives 429. |
| `RateLimiting__WebhookPermitsPerMinute` | No | `1200` | Requests per minute per client address for provider webhooks. |
| `ForwardedHeaders__Enabled` | No | `false` | Set to `true` behind a reverse proxy so the app sees the real client address and scheme. See section 9. |
| `ForwardedHeaders__KnownProxies__0` | With the setting above | a proxy on the same machine | The address of a proxy to trust. Add `__1`, `__2` for more. |
| `ForwardedHeaders__KnownNetworks__0` | No | — | An address range to trust, for example `10.0.0.0/8`. |
| `Hangfire__DashboardEnabled` | No | `false` | Serves the background job dashboard at `/hangfire`, to requests from the same machine only. |
| `Ocr__TesseractPath` | No | `/usr/bin/tesseract` | Absolute path to the Tesseract binary. |
| `Ocr__Languages` | No | `eng+ara` | OCR language codes to process. |
| `Ocr__MaxFileSizeMb` | No | `25` | Maximum upload size per document file in MB. |
| `Ocr__MaxPages` | No | `50` | Pages read from one PDF; later pages are ignored. |
| `Ocr__MaxDocumentSeconds` | No | `300` | Time one document may take in total before it is stopped. |
| `Ocr__MaxConcurrentDocuments` | No | `2` | Documents read at once. Others wait up to `Ocr__QueueSeconds` (default `30`), then the upload is left queued and the caller gets `ocr_busy`. |
| `Ocr__MaxPagePixels` | No | `40000000` | Pixels in one rendered PDF page. Larger pages are rendered at a lower resolution, down to `Ocr__MinRenderDpi` (default `100`), and skipped below that. |
| `Ocr__MaxImagePixels` | No | `60000000` | Pixels in an uploaded picture; larger ones are refused. |
| `Ocr__MaxTextCharacters` | No | `2000000` | Characters read from a plain text file. |
| `Ocr__MaxOutputMegabytes` | No | `20` | Output accepted from Tesseract before it is stopped. |
| `Ocr__EnhanceImages` | No | `true` | Clean each page picture before reading it: straighten it, raise its contrast, enlarge small ones, and try other settings when the reading scores low. `false` hands the original picture to Tesseract once. |
| `Ocr__DetectOrientation` | No | `true` | Turn a page that was scanned sideways or upside down the right way up. Needs `osd.traineddata` in the tessdata folder. |
| `Ocr__EnhanceMaxPixels` | No | `25000000` | Pictures with more pixels than this are read as they are, without cleaning, to bound memory. |
| `Ocr__TargetPageConfidence` | No | `85` | A page that scores this (0–100) or more is accepted at once. |
| `Ocr__MinPageConfidence` | No | `70` | A page that still scores below this after every attempt is marked low-confidence for a person to check. |
| `Ocr__MaxAttemptsPerPage` | No | `6` | The most readings of one page (original, denoised, sharpened and de-specked, black-and-white, other layouts) while its score stays low. A page that reads like noise is also tried turned, up to three more readings. |
| `Ocr__SecondEngine` | No | `true` | Ask a second engine (PaddleOCR models, run inside the app, no download at run time) to read a page that is still weak after every attempt. Its reading is kept only if it scores better. It reads Latin script only and is never given a page where Arabic was found. Set `false` to save the memory its models take once loaded. |
| `Llm__Provider` | No | `Ollama` | The language model that reads document fields for workspaces that switch AI reading on. `Ollama` runs on your own computer or network; `None` turns AI reading off everywhere. Nothing is sent to a cloud service. |
| `Llm__BaseUrl` | No | `http://localhost:11434` | Where Ollama listens. From inside a container use `http://host.docker.internal:11434`. |
| `Llm__Model` | No | `qwen2.5:7b-instruct` | The model name as `ollama list` shows it. An instruction-tuned model of 7B or more reads fields reliably; install it with `ollama pull qwen2.5:7b-instruct` (about 4.7 GB). |
| `Llm__TimeoutSeconds` | No | `300` | The longest one model call may take. A 7B model on a CPU needs about a minute per page of text. |
| `Llm__MaxInputChars` | No | `9000` | The most characters of a document sent to the model in one call; a longer document is cut after this many. |
| `Llm__NumCtx` | No | `6144` | The model's context window in tokens. It must hold the instructions, the document text and the answer. |
| `Llm__KeepAlive` | No | `30m` | How long Ollama keeps the model in memory after a call, so the next document does not wait for it to load. |
| `Hangfire__WorkerCount` | No | `20` | Number of concurrent background job worker threads. |

---

## 3b. Reading documents with AI

Each workspace defines, under **Settings > Document types**, the fields it wants from each kind of document (a name, a description in plain words, the kind of value, and optionally the contact, account or deal field an approved value is saved to). **Add ready-made types** creates a starting set (invoice, receipt, trade licence, Emirates ID, passport, appointment receipt, CV, contract) to edit.

AI reading is **off for every workspace until its administrator switches it on** (the card at the top of Document types). It needs a model on the server:

1. Install [Ollama](https://ollama.com) on the machine that runs MindLift, or on one it can reach.
2. If the system drive is short of space, point Ollama's model folder elsewhere first: set the user environment variable `OLLAMA_MODELS` (for example `D:\ollama\models`) and restart Ollama.
3. Install the model: `ollama pull qwen2.5:7b-instruct`.
4. Open Document types. The card says whether the model is ready. Switch AI reading on.

How a document is read: the text comes from the PDF itself or from Tesseract (unchanged); the model is then asked for exactly the fields the document type defines. Each value is looked up in the document's own text afterwards, and a value that cannot be found there is marked *not found in the document text* for a person to check. Nothing is saved to a record until a person approves the document. Documents are read in the background (the list shows *Waiting to be read*, then *Reading*), so an upload never waits for the model. If the model is off, not reachable, too slow or answers badly, the document is still read with each field's anchor or pattern and a note says why.

On a CPU-only computer a 7B model needs roughly a minute per document; the first document after a restart also waits for the model to load. Only one document is read by the model at a time.

---

## 4. Database Maintenance & Backups

### Automated Migrations
When `Database__MigrateOnStartup` is set to `true`, the application applies all database schema migrations upon startup during container initialization.

### Backing Up the PostgreSQL Database
Execute a full logical backup using `pg_dump`:
```bash
docker exec -t mindlift-prod-postgres pg_dump -U mindlift_admin -d mindlift -F c -b -v -f /var/lib/postgresql/data/mindlift_backup_$(date +%Y%m%d_%H%M%S).dump
```

### Restoring from Backup
```bash
# Drop connections and restore into target database
docker exec -t mindlift-prod-postgres pg_restore -U mindlift_admin -d mindlift --clean --if-exists /var/lib/postgresql/data/your_backup_file.dump
```

---

## 5. Background Jobs (Hangfire)

MindLift CRM executes asynchronous workflows through scheduled recurring jobs:

| Job Key | Schedule | Description |
|---|---|---|
| `trial-expiry` | Daily at 06:00 UTC | Scans expiring tenant trials and sends reminder or suspension notices. |
| `refresh-token-cleanup` | Daily at 03:00 UTC | Purges expired or revoked JWT refresh tokens. |
| `document-expiry` | Daily at 04:00 UTC | Scans approved documents expiring in 60, 30, and 7 days and triggers alerts. |
| `booking-reminders` | Every 5 minutes | Dispatches meeting and appointment reminder emails and notifications. |
| `activity-reminders` | Every minute | Checks upcoming tasks and calls due within the user's notification window. |
| `daily-digest` | Hourly | Aggregates daily summaries for tenants configured for digest emails. |

The Hangfire dashboard is off by default. Set `Hangfire__DashboardEnabled=true` to serve it at `/hangfire`; even then it answers only requests from the same machine, so reach it through an SSH tunnel and never expose it publicly.

---

## 6. OCR Pipeline & Tesseract Setup

The OCR pipeline extracts structured information from uploaded receipts, trade licenses, invoices, and passports:
1. When running inside Docker, Tesseract OCR 5 with English and Arabic data (`tesseract-ocr`, `tesseract-ocr-eng`, `tesseract-ocr-ara`) is automatically installed.
2. The health check at `GET /health` verifies the OCR engine.
   - If Tesseract is operational, status is `Healthy`.
   - If Tesseract is absent on the host, status is `Degraded` and the pipeline falls back to digital text extraction.
3. Passports with Machine Readable Zones (MRZ TD3) automatically parse check digits (7-3-1 algorithm), names, dates, and document numbers.
4. Arabic documents normalize Arabic-Indic digits (`٠-٩`) to ASCII numbers.

---

## 7. Security & Hardening Checklist

- [x] **File Upload Restrictions:** Maximum file size enforced at 25 MB; executable/script extensions (`.exe`, `.bat`, `.sh`, `.ps1`, etc.) are rejected with 400 Bad Request.
- [x] **Cross-Realm Protection:** Portal tokens carry their own audience (`mindlift-portal`) and a customer claim, and are checked by a separate authentication scheme, so staff tokens cannot reach portal customer endpoints and the other way round. Both kinds are signed with the same `Jwt__SigningKey`; there is no separate portal key.
- [x] **Non-Root Execution:** The Docker container executes with unprivileged user permissions (`$APP_UID`).
- [x] **Token Revocation:** Automatic refresh token rotation with family reuse breach detection revokes compromised sessions immediately.
- [x] **Rate Limiting:** Sign-in, sign-up, magic links, public booking, website forms and webhooks are limited per client address (settings above). Account lockout also applies after 5 wrong passwords, for staff and for portal customers.
- [x] **Webhook Authentication:** WhatsApp (Meta) webhooks must carry a valid `X-Hub-Signature-256`, and Twilio webhooks a valid `X-Twilio-Signature`, checked against the secret saved on that channel. A channel with no secret never accepts webhooks, and a verified request can only reach its own workspace.
- [x] **Encrypted Secrets:** Mail passwords, provider tokens and webhook secrets are encrypted at rest with AES-256 and an integrity check, using `Security__SecretKey`. Set it explicitly in production.
- [x] **Upload Checks:** Document uploads accept only listed file types, check that the file contents match the type, and are stored with the content type of their extension.
- [x] **Security Headers:** Every response carries `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and a Content-Security-Policy that allows only the app's own scripts (`Security__CspMode`: `Enforce` by default in production, `ReportOnly` or `Off`). Behind a TLS proxy the app also redirects plain HTTP to HTTPS and sends `Strict-Transport-Security` (`Security__RequireHttps`).

---

## 8. Provider Webhooks (WhatsApp and Twilio SMS)

1. In Settings, add the channel and enter its secret: the **app secret** for WhatsApp (from the Meta app), or the **auth token** for Twilio. Save the WhatsApp **phone number ID** as well.
2. Register the webhook with the provider:
   - WhatsApp: `https://<your address>/api/v1/webhooks/whatsapp`, with the same token as `Communication__WhatsAppVerifyToken`.
   - Twilio: `https://<your address>/api/v1/webhooks/twilio`. The address must match `Communication__PublicBaseUrl` (or `App__BaseUrl`) exactly, including `https`.
3. A request that fails verification is refused with 401 and nothing is stored. If messages stop arriving, check that the secret on the channel matches the provider and, for Twilio, that the public address is the one the provider calls.

---

## 9. Running Behind a Reverse Proxy

By default the app uses the address the connection comes from. Behind a proxy that is the proxy, so every customer would share one rate limit. Set `ForwardedHeaders__Enabled=true` and name the proxy with `ForwardedHeaders__KnownProxies__0` (or a range in `ForwardedHeaders__KnownNetworks__0`). The app then reads the client address and scheme from `X-Forwarded-For` and `X-Forwarded-Proto`, but only from the proxies you named. With none named, only a proxy on the same machine is trusted. An address that is not valid stops the app at startup with a message saying which one.

## Platform console (for the owner of the product)

The platform owner signs in at the normal sign-in page and lands on `/platform`. Create the owner once:

```
dotnet run --project src/MindLift.Api -- seed-platform-admin --email you@example.com --password "…"
```

- **Overview**: company and revenue numbers, signups, what needs attention.
- **Companies**: every company with its usage and health. Open one to choose its **modules** (each feature is a module; core modules are always on), its **plan and limits** (user limit, trial, AI reading), and to keep private notes.
- A module switched off disappears from that company's menu and its API refuses it straight away.
- The console shows company-level numbers only; it never opens a company's own records. Every change is listed under **Activity**.

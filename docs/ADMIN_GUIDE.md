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
   JWT_SIGNING_KEY=Minimum32CharacterSecretKeyForStaffTokens12345!
   PORTAL_JWT_SIGNING_KEY=Minimum32CharacterSecretKeyForPortalTokens12345!
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
| `ConnectionStrings__DefaultConnection` | **Yes** | — | PostgreSQL connection string with host, port, database, user, and password. |
| `Database__MigrateOnStartup` | No | `true` | When true, applies pending EF Core migrations on application boot. |
| `Jwt__SigningKey` | **Yes** | — | HMAC-SHA256 secret key for signing staff access tokens (min 32 bytes). |
| `Jwt__Issuer` | No | `https://app.mindlift.io` | Staff JWT issuer URL. |
| `Jwt__Audience` | No | `mindlift-api` | Staff JWT audience string. |
| `PortalJwt__SigningKey` | **Yes** | — | HMAC-SHA256 secret key for signing customer portal access tokens. |
| `PortalJwt__Issuer` | No | `https://app.mindlift.io` | Customer portal JWT issuer URL. |
| `PortalJwt__Audience` | No | `mindlift-portal` | Customer portal JWT audience string. |
| `Ocr__TesseractPath` | No | `/usr/bin/tesseract` | Absolute path to the Tesseract binary. |
| `Ocr__Languages` | No | `eng+ara` | OCR language codes to process. |
| `Ocr__MaxFileSizeMb` | No | `25` | Maximum upload size per document file in MB. |
| `Hangfire__WorkerCount` | No | `20` | Number of concurrent background job worker threads. |

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

In Development mode, the Hangfire dashboard is accessible at `/hangfire`. In production, access is restricted to authenticated platform administrators.

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

- [x] **Rate Limiting:** Protects sensitive authentication and sign-in routes against brute-force attacks.
- [x] **File Upload Restrictions:** Maximum file size enforced at 25 MB; executable/script extensions (`.exe`, `.bat`, `.sh`, `.ps1`, etc.) are rejected with 400 Bad Request.
- [x] **Cross-Realm Protection:** Portal tokens are signed with a distinct key and audience (`mindlift-portal`); staff tokens cannot access portal customer endpoints and vice-versa.
- [x] **Non-Root Execution:** The Docker container executes with unprivileged user permissions (`$APP_UID`).
- [x] **Token Revocation:** Automatic refresh token rotation with family reuse breach detection revokes compromised sessions immediately.

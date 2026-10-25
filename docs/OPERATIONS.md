# Running MindLift in production

How the production setup in `docker-compose.prod.yml` fits together, what to set, and how to keep it healthy.

## What runs

| Service | What it does |
| --- | --- |
| `api` | The web app and API. Runs the light background work (`default`, `messaging` queues) and migrates the database on start. |
| `worker` | The same image, taking only the heavy background work: reading documents (OCR, AI), reports and imports (`ocr`, `reports`, `imports` queues). It never slows the web app down. |
| `caddy` | The front door: ends TLS (certificates fetched and renewed automatically for `DOMAIN`) and passes traffic to the api. Ports 80 and 443; the api itself is not published. |
| `postgres` | The database. `max_connections=200`; the api pool is capped at 60 and the worker's at 40. |
| `redis` | Shared cache and live notifications across servers. Needed once more than one api runs. |
| `backup` | A full database dump every night, kept `BACKUP_KEEP_DAYS` days (14 by default) in the `backups` volume. |

Volumes: `postgres-data` (the database), `api-files` (uploaded files, unless S3 is used), `backups`, `redis-data`, `logs`.

## Settings you must give

`DOMAIN` (the host name, for example `crm.example.com`, pointing at this server; `localhost` to try the stack on one machine), `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `SECRET_KEY` (a different long random value), `APP_BASE_URL` (for example `https://crm.example.com`),
`SMTP_HOST` and `SMTP_FROM_ADDRESS` (plus `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_PORT` as your mail service needs).
The containers refuse to start without them.

The bundled caddy is trusted as the proxy by default, so visitor addresses and HTTPS are recognised. With a different proxy (nginx, Traefik, a load balancer, Cloudflare) set `FORWARDED_PROXY_ADDRESS` to it.
The app sends plain-HTTP requests to HTTPS and adds `Strict-Transport-Security` (`REQUIRE_HTTPS`, on by default here), and sends a Content-Security-Policy (`CSP_MODE`: `Enforce`, `ReportOnly` or `Off`).
Without it every visitor appears to come from the proxy, and the sign-in rate limit is shared by everyone. The app logs a warning at start when it is off.

## Optional settings

- **Files in a bucket**: `STORAGE_PROVIDER=S3` with `S3_BUCKET`, and either `S3_REGION` (Amazon) or `S3_SERVICE_URL` (Cloudflare R2, MinIO, Wasabi), and `S3_ACCESS_KEY` / `S3_SECRET_KEY`.
  Files then survive any server change and several servers share them.
- **Error tracking**: `SENTRY_DSN`. Every unexpected error is sent with the request and the company it happened in (no personal data).
- **Searchable logs**: `SEQ_URL` (and `SEQ_API_KEY`). Logs also go to daily JSON files in the `logs` volume, kept 14 days.
  Every log line of a request carries `CorrelationId`, `TenantId` and `UserId`. The correlation id is returned to the browser in the `X-Correlation-Id` header, so a customer can quote it.
- **Mail encryption**: port 465 is encrypted from the start, and `SMTP_STARTTLS=true` (the default here) requires STARTTLS. A mail server on another machine is never used without encryption unless `Smtp__AllowInsecure=true`.
  The WhatsApp and Twilio "Test connection" buttons make a real call to the provider and report what it answers; with test credentials (starting `test_` or `mock_`) they only simulate.
- **Billing**: `BILLING_CURRENCY` and `BILLING_PAYMENT_INSTRUCTIONS` (your bank details, shown to companies on their Billing page until a payment gateway is connected).
- **Email protection**: `EMAIL_DAILY_LIMIT` (automated mail per company per day, 500 by default) and `EMAIL_SPF_INCLUDE` (the SPF include your mail service gives you, shown to companies when they verify their sending domain).

## Health checks

- `/health/live`: the process answers.
- `/health/ready`: it can also reach the database. Point the load balancer and an uptime monitor (for example Better Stack or UptimeRobot) here.
- `/health`: everything, including OCR.

## Backups and restore

The `backup` service writes `/backups/mindlift-YYYYMMDD-HHMMSS.dump` every night. **Copy the backups volume off the server** (another region, another provider):
a backup on the same disk as the database does not survive the disk.

Restore a dump into an empty database:

```
docker compose -f docker-compose.prod.yml exec backup pg_restore --clean --if-exists --dbname="$PGDATABASE" /backups/mindlift-YYYYMMDD-HHMMSS.dump
```

**Prove it works.** Run the restore drill after setting up, and again every month. It loads a dump into a scratch database, checks the key tables have rows, and reports how long it took (your recovery time):

```
docker compose -f docker-compose.prod.yml exec backup sh /scripts/restore-drill.sh /backups/mindlift-YYYYMMDD-HHMMSS.dump
```

**Off-site copy.** `docker compose -f docker-compose.prod.yml --profile offsite up -d` also starts `backup-offsite`, which copies new dumps to any rclone-supported bucket every 6 hours
(set `OFFSITE_ACCESS_KEY`, `OFFSITE_SECRET_KEY`, `OFFSITE_PATH`, and `OFFSITE_REGION` or `OFFSITE_ENDPOINT`; server-side encryption is on by default).

For one company only, use its data export (below) instead of restoring the whole database.

For point-in-time recovery, use a managed PostgreSQL (Amazon RDS, Azure Database, DigitalOcean, Neon) with continuous backups instead of the `postgres` container.

## One company's data

- **Export**: the company (Settings, Your data) or the platform owner (company page, Data) prepares a ZIP of every table and file of that company. It is ready in the background and downloadable for 7 days through a 10-minute signed link.
- **Removal**: the platform owner (company page, Data) removes a company and all its data for good after typing its address. The platform audit trail keeps a record that it happened.

## Support access

The platform owner can open a **read-only** support view of a company (company page, Support view) for up to 30 minutes, with a written reason.
Nothing can be changed from it; the reason and every page viewed are kept in the audit trail and on the company's record.

## Scaling

- Run more `api` containers behind the load balancer once Redis is set (cache and live notifications are shared through it). Start-up migrations take a database lock, so only one migrates at a time.
- Run more `worker` containers when documents or reports queue up.
- If the database runs short of connections, put PgBouncer (session mode) in front of it, or use a managed pooler (RDS Proxy). Transaction mode is not supported: start-up migrations and the background job server rely on session features.

## Retention

- Record-change history (`audit_logs`) older than `Audit__RetentionDays` (730 by default; 0 keeps everything) is removed nightly.
- Read notifications and expired sign-in tokens are cleaned up nightly.

## Calendar sync (Google and Microsoft)

People connect their own calendar under Calendar, Connect calendars. Their meetings are copied to it, and the times already taken there block booking.
Each service stays off until you give its client id and secret (`CALENDAR_GOOGLE_CLIENT_ID`, `CALENDAR_GOOGLE_CLIENT_SECRET`, `CALENDAR_MICROSOFT_CLIENT_ID`,
`CALENDAR_MICROSOFT_CLIENT_SECRET`, optional `CALENDAR_MICROSOFT_TENANT`). Register the redirect address `APP_BASE_URL/calendar/oauth/google/callback`
(and `.../microsoft/callback`) with each service.

- Google: create an OAuth client (Web application) in Google Cloud Console, enable the Google Calendar API, and add the scopes `calendar.events` and `calendar.readonly`.
- Microsoft: register an app in Microsoft Entra, add the delegated permissions `Calendars.ReadWrite`, `User.Read` and `offline_access`, and create a client secret.

A calendar feed (.ics) needs no set-up: any person can make one for Apple Calendar or any app that subscribes to an address.

## Tickets and campaigns

Both are modules like the others: switch them on or off per company in the platform console, or leave them to a plan. Campaigns send through the same
protection as other automated mail (daily limit per company, unsubscribe link, do-not-email list), and what does not fit in a day goes out the next day.

## Arabic

The Arabic OCR model is the higher-accuracy one (`tools/tessdata/ara.traineddata`). Pages that are mostly Arabic are read with Arabic alone as well as with Arabic and
English together, and the better reading is kept. Arabic lines are put in reading order, digits are written as ordinary digits, and direction marks are removed.
The platform console can be switched to Arabic (right to left) from its top bar.

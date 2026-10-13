# MindLift CRM — REST API Reference

The MindLift API provides a fully-featured, secure RESTful interface for CRM workflows, CPQ calculations, document intelligence, analytics, and customer portal self-service.

Interactive API documentation and schema exploration is available in Development mode at `/scalar/v1` and OpenAPI specification at `/openapi/v1.json`.

---

## 1. Authentication & Multi-Tenancy

### Dual Authentication Schemes
The API implements two mutually isolated authentication realms:

1. **Staff Realm (`Bearer` scheme):**
   - Intended for tenant team members, managers, and administrators.
   - Header: `Authorization: Bearer <Staff_JWT>`
   - Audience: `mindlift-api`
   - Authorization: Enforced via `[HasPermission(AppModule, PermissionAction)]`.

2. **Customer Portal Realm (`Portal` scheme):**
   - Intended for customer contacts accessing their shared documents and requests.
   - Header: `Authorization: Bearer <Portal_JWT>`
   - Audience: `mindlift-portal`
   - Scoped strictly to the contact's owned or shared records.
   - Web clients automatically receive and send the `ml_portal_refresh` HttpOnly SameSite=Strict cookie; native/mobile clients pass `X-Client: mobile` to receive refresh tokens in the JSON response payload.

### Tenant Resolution
For staff requests, the tenant scope is resolved automatically from the JWT `tid` claim.
For unauthenticated portal requests (such as branding or customer login), the tenant is identified by the company slug in the URL path (`/api/v1/portal/{slug}/...`).

---

## 2. API Endpoints Catalog

### Authentication & Tenant Onboarding

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/auth/signup` | Creates a new tenant workspace and root administrator account. |
| `POST` | `/api/v1/auth/login` | Authenticates a staff user with email and password. |
| `POST` | `/api/v1/auth/refresh` | Rotates staff access and refresh tokens. |
| `GET` | `/api/v1/auth/me` | Returns current staff profile, tenant details, and granted permissions. |
| `GET` | `/api/v1/onboarding/industries` | Lists available industry templates and default configurations. |
| `POST` | `/api/v1/onboarding/apply-template` | Configures the tenant with pre-built pipeline stages and fields. |

### Customer Portal

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/portal/{slug}/branding` | Retrieves tenant branding (name, logo, theme colors, support info). |
| `POST` | `/api/v1/portal/{slug}/auth/login` | Customer credentials sign-in. |
| `POST` | `/api/v1/portal/{slug}/auth/magic-link` | Requests a passwordless sign-in link via email. |
| `POST` | `/api/v1/portal/{slug}/auth/magic-verify` | Verifies a magic link token. |
| `POST` | `/api/v1/portal/{slug}/auth/accept-invite` | Sets password for a newly invited customer contact. |
| `POST` | `/api/v1/portal/{slug}/auth/refresh` | Rotates portal refresh tokens (cookie or mobile body). |
| `POST` | `/api/v1/portal/{slug}/auth/logout` | Revokes the current customer refresh token family. |
| `GET` | `/api/v1/portal/home` | Customer dashboard: shared documents, open requests, and metrics. |
| `GET` | `/api/v1/portal/documents` | Lists shared documents with optional category and search filters. |
| `GET` | `/api/v1/portal/documents/{id}/file` | Downloads a shared document file. |
| `GET` | `/api/v1/portal/requests` | Lists customer document upload requests. |
| `POST` | `/api/v1/portal/requests/{id}/upload` | Customer upload answering a document request. |
| `GET` | `/api/v1/portal/me` | Customer profile information and language preference. |
| `PUT` | `/api/v1/portal/me` | Updates customer profile settings (e.g. language). |
| `POST` | `/api/v1/portal/me/password` | Changes the customer portal password. |

### CRM Core Modules

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/contacts` | Search and filter tenant contacts. |
| `POST` | `/api/v1/contacts` | Creates a new contact record. |
| `GET` | `/api/v1/contacts/{id}/portal` | Returns the portal access status for a contact. |
| `POST` | `/api/v1/contacts/{id}/portal/invite` | Invites a contact to access the Customer Portal. |
| `GET` | `/api/v1/accounts` | Lists companies and accounts. |
| `POST` | `/api/v1/accounts` | Creates a new company record. |
| `GET` | `/api/v1/leads` | Lists captured sales leads with status and rating. |
| `POST` | `/api/v1/leads/{id}/convert` | Converts a qualified lead into an Account, Contact, and Deal. |
| `GET` | `/api/v1/deals` | Lists deals across pipeline stages. |

### CPQ, Invoices & Payments

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/products` | Lists products and subscription services. |
| `POST` | `/api/v1/products` | Creates or updates a product catalog entry. |
| `GET` | `/api/v1/quotes` | Lists price quotations. |
| `POST` | `/api/v1/quotes` | Generates a quote with line items, tax, and discount rules. |
| `POST` | `/api/v1/quotes/{id}/convert-to-invoice` | Automatically generates a billable invoice from an accepted quote. |
| `GET` | `/api/v1/invoices` | Lists customer invoices and aging status. |
| `POST` | `/api/v1/invoices/{id}/payments` | Records partial or full customer payment against an invoice. |

### Documents & OCR Intelligence

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/documents` | Lists business documents filtered by account or deal. |
| `POST` | `/api/v1/documents/upload` | Uploads a document (PDF, PNG, JPG, WEBP) with optional links. |
| `POST` | `/api/v1/documents/{id}/ocr` | Triggers OCR extraction and structured field parsing. |
| `POST` | `/api/v1/documents/{id}/review` | Approves or rejects a document with optional record field mapping. |
| `PUT` | `/api/v1/documents/{id}/fields` | Updates extracted OCR fields and detected totals. |
| `POST` | `/api/v1/documents/{id}/reprocess` | Re-executes OCR processing on an existing document. |
| `GET` | `/api/v1/documents/{id}/file` | Downloads the original file payload. |
| `DELETE` | `/api/v1/documents/{id}` | Soft-deletes a business document. |

### Reports & Analytics

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/reports` | Lists standard and custom reports. |
| `GET` | `/api/v1/reports/sources` | Lists queryable data sources and field definitions. |
| `POST` | `/api/v1/reports/run` | Executes a dynamic report query with aggregation and filtering. |
| `GET` | `/api/v1/reports/{id}/export?format=csv` | Exports report results to CSV format. |
| `GET` | `/api/v1/reports/{id}/export?format=xlsx` | Exports report results to Microsoft Excel format. |

---

## 3. Error Handling

MindLift APIs return standardized error responses based on RFC 7807 (ProblemDetails):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "File extension '.exe' is prohibited for security reasons.",
  "code": "invalid_extension"
}
```

Common status codes:
- `200 OK`: Successful retrieval or mutation.
- `201 Created`: Resource successfully created.
- `400 Bad Request`: Validation failure or malformed payload.
- `401 Unauthorized`: Missing or invalid authentication token.
- `403 Forbidden`: Insufficient permissions or cross-tenant access attempt.
- `404 Not Found`: Target resource does not exist in the tenant scope.
- `409 Conflict`: Concurrency conflict or duplicate unique value.
- `422 Unprocessable Entity`: Business domain rule violation.

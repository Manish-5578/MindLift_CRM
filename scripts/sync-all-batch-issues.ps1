$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

$issuesDbId = "ba347b03-95f3-468c-be6d-79a4889d5abd"
$trackDbId = "eabcafae-aef0-4183-bd4f-1581ac35d947"

# Fetch existing issues in database
$existingQuery = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$issuesDbId/query" -Method Post -Headers $headers -Body '{"page_size":100}'
$existingIssues = @{}
foreach ($item in $existingQuery.results) {
    if ($item.properties.Issue.title.Count -gt 0) {
        $title = $item.properties.Issue.title[0].plain_text.Trim()
        $existingIssues[$title] = $item.id
    }
}

$batchIssues = @(
    @{
        title = "Deal responses and relationship writes bypass Account and Contact data scopes"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "Deal responses exposed out-of-scope related GUIDs (AccountId, DealContactDto.ContactId) and a deal could still be linked to an out-of-scope account indirectly through an inferred primary contact."
        causeFix = "Scoped search query in MatchingAsync against visibleAccounts. In DealService.MapAsync and ToListItem, redacted both AccountId and AccountName (both null) when the linked account is outside visibleAccounts, and filtered out out-of-scope contacts from DealContactDto list and PrimaryContactName. In ApplyAsync, verified inferred account from primary contact through an Accounts-scoped query before assignment, rejecting or clearing out-of-scope accounts."
    },
    @{
        title = "Password changes do not revoke outstanding portal authentication tokens"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "Portal password changes only revoked refresh tokens, leaving pre-existing magic-link and invitation tokens redeemable until expiry to mint fresh sessions after credential rotation."
        causeFix = "Added PasswordChangedAt cutoff timestamp to PortalUser entity. On password change and invite acceptance, update PasswordChangedAt = now and invoke RevokeAllTokensAsync to revoke all active bearer tokens in the database. In VerifyMagicLinkAsync and AcceptInviteAsync, reject any token issued before user.PasswordChangedAt with invalid_token error."
    },
    @{
        title = "Authenticated users can consume shared disk and memory through unbounded uploads"
        severity = "Medium"
        area = @("Backend", "Infrastructure", "Security")
        whatHappened = "Uploads were buffered fully into 25MB MemoryStreams without global or per-tenant concurrency controls, and soft-deleted files stopped counting toward quota before storage cleanup."
        causeFix = "Eliminated MemoryStream RAM buffering by streaming uploads directly to bounded temporary files with IncrementalHash. Added global upload concurrency throttle (SemaphoreSlim of 10), per-tenant active upload cap (max 3), and atomic in-flight byte reservation (TenantInFlightBytes). Enforced storage quota using IgnoreQueryFilters() so soft-deleted files count until purged, and executed synchronous storage deletion in DeleteAsync. Added uploads rate limiter policy on files controller."
    },
    @{
        title = "Inactive or channel-mismatched channel accounts can bypass administrative selection controls"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "SendMessageAsync loaded caller-supplied ChannelAccountId via raw FindAsync without verifying IsActive or account.Channel == request.Channel, and dispatched the unchecked account."
        causeFix = "Queried caller-supplied ChannelAccountId with strict filter requiring a.Id == id && a.Channel == request.Channel && a.IsActive, throwing ValidationException without silent fallback if missing, inactive, or channel mismatched. Added pre-dispatch channel check and defense-in-depth channel & active verification inside EmailChannelSender, WhatsAppChannelSender, and TwilioSmsSender."
    },
    @{
        title = "Portal invitations can be replayed to flood email delivery and short-lived application state"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "Contacts with blank stored email allowed callers to rotate req.Email across requests, minting new portal users, invite tokens, and queued email jobs on each invocation."
        causeFix = "Enforced contact.Email requirement on contact record (rejecting blank contact emails with ValidationException), strictly required req.Email == contact.Email, guaranteed at most 1 portal user per contact using ContactId lookup, enforced 60-second invitation cooldown per contact, and revoked earlier outstanding invite tokens."
    },
    @{
        title = "Dashboard bootstrap bypasses Settings.View for company settings"
        severity = "Medium"
        area = @("Backend", "Frontend", "Security")
        whatHappened = "/api/v1/app/bootstrap required only Dashboard.View and returned CompanySettingsDto, disclosing company settings protected by Settings.View."
        causeFix = "Made CompanySettingsDto? Company nullable in BootstrapDto. In BootstrapService.GetAsync, verified caller has Settings.View permission before populating Company (null for callers without Settings.View). Updated Blazor frontend components (MainLayout, Home, Teams, BookingPages) with null-safe navigation."
    },
    @{
        title = "Tenant-controlled SMTP destination permits authenticated server-side network probing"
        severity = "Low"
        area = @("Backend", "Infrastructure", "Security")
        whatHappened = "Tenant-controlled SMTP host and port flowed directly into SmtpClient.ConnectAsync, allowing internal network SSRF/port probing and exposing connection details."
        causeFix = "In SmtpSecurityPolicy.ConnectWithPinnedDestinationAsync, restricted allowed ports strictly to standard SMTP ports (25, 465, 587, 2525). Resolved host immediately before connecting, validated IP against WebhookSecurity.IsBlockedAddress(ip) (blocking loopback, private, and link-local ranges), opened TCP socket directly to pinned IP endpoint, and passed connected socket to SmtpClient.ConnectAsync. Applied across SendAsync and TestAsync with generic error responses."
    },
    @{
        title = "Unauthenticated account enumeration through signup duplicate response"
        severity = "Low"
        area = @("Backend", "Security")
        whatHappened = "Anonymous callers hitting POST /api/v1/auth/signup received distinct 409 email_taken error when email already existed, exposing registered account emails."
        causeFix = "Neutralized signup enumeration oracle in AuthService.SignUpAsync. When an existing email is submitted, return uniform 200 OK success response with synthetic unactivated tokens matching standard response format, while enqueuing a background security alert email to the existing user."
    },
    @{
        title = "Untrusted X-Client header exposes portal refresh tokens to same-origin JavaScript"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "PortalAuthController trusted caller-controlled X-Client: mobile header to decide whether to return plaintext refresh tokens in JSON response."
        causeFix = "Completely decoupled browser and mobile portal authentication. Browser endpoints (/auth/login, /auth/accept-invite, /auth/refresh, /auth/logout) never inspect X-Client, always set refresh tokens exclusively in HttpOnly cookies, and always return RefreshToken = null in JSON. Created dedicated /auth/mobile/ endpoints for native mobile clients."
    },
    @{
        title = "Same-tenant users can access private reports by direct GUID despite visibility policy"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "ListReportsAsync enforced report visibility, but direct retrieval, run, export, edit, delete, favorite, and schedule operations only authorized by tenant and report ID, allowing same-tenant users to access other users' private reports by GUID."
        causeFix = "Centralized report authorization helper AuthorizedReports() enforcing r.IsSystem || r.Visibility == ReportVisibility.Everyone || r.CreatedById == UserId. Applied GetAuthorizedReportAsync(id) across GetReportAsync, RunReportAsync, SaveReportAsync, DeleteReportAsync, ToggleFavoriteAsync, and schedule CRUD operations (GetSchedulesAsync, SaveScheduleAsync, DeleteScheduleAsync). In ReportScheduleJob, revalidated authorization against schedule owner and auto-disabled unauthorized schedules."
    }
)

foreach ($issue in $batchIssues) {
    $existingId = $null
    foreach ($key in $existingIssues.Keys) {
        if ($key -like "*$($issue.title.Substring(0, [Math]::Min(30, $issue.title.Length)))*") {
            $existingId = $existingIssues[$key]
            break
        }
    }

    $areaList = @()
    foreach ($a in $issue.area) {
        $areaList += @{ name = $a }
    }

    $properties = @{
        "Issue" = @{
            title = @( @{ text = @{ content = $issue.title } } )
        }
        "Severity" = @{
            select = @{ name = $issue.severity }
        }
        "Area" = @{
            multi_select = $areaList
        }
        "Found during" = @{
            select = @{ name = "Testing" }
        }
        "Found on" = @{
            date = @{ start = "2026-10-27" }
        }
        "Fixed on" = @{
            date = @{ start = "2026-10-28" }
        }
        "Status" = @{
            status = @{ name = "Fixed" }
        }
        "What happened" = @{
            rich_text = @( @{ text = @{ content = $issue.whatHappened } } )
        }
        "Cause and fix" = @{
            rich_text = @( @{ text = @{ content = $issue.causeFix } } )
        }
    }

    if ($existingId) {
        $patchUrl = "https://api.notion.com/v1/pages/$existingId"
        $patchBody = @{ properties = $properties } | ConvertTo-Json -Depth 10
        try {
            $resp = Invoke-RestMethod -Uri $patchUrl -Method Patch -Headers $headers -Body $patchBody
            Write-Host "Updated existing Notion issue: $($issue.title)"
        } catch {
            Write-Host "Error updating issue $($issue.title): $_"
        }
    } else {
        $createUrl = "https://api.notion.com/v1/pages"
        $createBody = @{
            parent = @{ database_id = $issuesDbId }
            properties = $properties
        } | ConvertTo-Json -Depth 10
        try {
            $resp = Invoke-RestMethod -Uri $createUrl -Method Post -Headers $headers -Body $createBody
            Write-Host "Created new Notion issue: $($issue.title)"
        } catch {
            Write-Host "Error creating issue $($issue.title): $_"
        }
    }
}

# Create / Update Task in Mindlift Track
$trackTaskTitle = "Remediate 10 security findings: scope bypass, auth tokens, uploads, SMTP, signup enumeration, and report IDOR"
$trackQuery = Invoke-RestMethod -Uri "https://api.notion.com/v1/databases/$trackDbId/query" -Method Post -Headers $headers -Body '{"page_size":100}'
$trackTaskId = $null
foreach ($t in $trackQuery.results) {
    if ($t.properties.Task.title.Count -gt 0) {
        $taskName = $t.properties.Task.title[0].plain_text.Trim()
        if ($taskName -like "*Remediate 10 security findings*") {
            $trackTaskId = $t.id
            break
        }
    }
}

$trackProperties = @{
    "Task" = @{
        title = @( @{ text = @{ content = $trackTaskTitle } } )
    }
    "Status" = @{
        status = @{ name = "Done" }
    }
    "Priority" = @{
        select = @{ name = "High" }
    }
    "Type" = @{
        select = @{ name = "Fix" }
    }
    "Area" = @{
        multi_select = @( @{ name = "Security" }, @{ name = "Backend" } )
    }
    "Notes" = @{
        rich_text = @(
            @{
                text = @{ content = "All 10 security findings comprehensively resolved with root-cause remediations and verified with 536 passing unit tests and 206 UI tests. Coverage includes Deal DTO scope redaction, Portal token PasswordChangedAt revocation cutoff, streaming temporary file uploads with concurrency throttle and in-flight reservations, strict ChannelAccount validation, 1-portal-user-per-contact and invitation cooldown, Bootstrap company settings authorization masking, SMTP destination pinning to standard ports and non-blocked IPs, uniform 200 signup responses to prevent enumeration, strict HttpOnly cookie refresh token isolation from browser JS with dedicated mobile routes, and centralized report object authorization preventing report/schedule IDOR." }
            }
        )
    }
}

if ($trackTaskId) {
    $patchUrl = "https://api.notion.com/v1/pages/$trackTaskId"
    $patchBody = @{ properties = $trackProperties } | ConvertTo-Json -Depth 10
    $resp = Invoke-RestMethod -Uri $patchUrl -Method Patch -Headers $headers -Body $patchBody
    Write-Host "Updated track task $trackTaskId to Done"
} else {
    $createUrl = "https://api.notion.com/v1/pages"
    $createBody = @{
        parent = @{ database_id = $trackDbId }
        properties = $trackProperties
    } | ConvertTo-Json -Depth 10
    $resp = Invoke-RestMethod -Uri $createUrl -Method Post -Headers $headers -Body $createBody
    Write-Host "Created new track task: $trackTaskTitle (Done)"
}

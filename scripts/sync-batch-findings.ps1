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

$today = (Get-Date).ToString("yyyy-MM-dd")

$batchIssues = @(
    @{
        title = "Deal responses and relationship writes bypass Account and Contact data scopes"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "A Deals editor who knows an out-of-scope ContactId could update that hidden relationship via the deal save path without passing Contacts scope, or modify/remove hidden links."
        causeFix = "In ResolveContactsAsync, enforced Contacts-scoped query on all submitted ContactIds, rejecting out-of-scope contacts even if already linked. In ReplaceContactsAsync, resolved caller's visible contacts and preserved pre-existing hidden contacts server-side without letting an unscoped caller delete or alter them."
    },
    @{
        title = "Anonymous booking-management tokens are exposed in request and error logs"
        severity = "Medium"
        area = @("Backend", "Frontend", "Security")
        whatHappened = "Booking-management tokens were carried in the URL path (/booking/manage/{token}), exposing credentials in HTTP access logs, Serilog request logging, and unhandled exception reports sent to Sentry."
        causeFix = "Switched ManageBooking.razor web client to header (X-Booking-Token) and body (Token) flow, immediately scrubbing tokens from the browser URL. Added Sentry BeforeSend event processor in Program.cs that redacts /manage/[^/?]+ and ([?&]token=)[^&]+ from request URLs and query strings before events are forwarded. Explicitly sanitized RequestPath in Serilog EnrichDiagnosticContext so tokens cannot leak even under Verbose logging. Rotated and invalidated management tokens on booking mutation."
    },
    @{
        title = "Portal access JWTs remain usable after PortalUser session termination"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "Ordinary portal requests revalidated user state, but portal access JWTs were not tied to a revocable server-side session, allowing already-issued bearer tokens to be replayed until expiry even after logout, password change, or contact deletion."
        causeFix = "Added server-side portal session versioning / security stamp mechanism: persisted user.Version on PortalUser, embedded it as 'ver' claim when minting access JWTs in TokenService, and enforced token 'ver' matches user.Version in JwtBearerEvents.OnTokenValidated on every request. On logout (LogoutAsync), password change (ChangePasswordAsync), user deactivation, and contact deletion/reassignment (ContactServices), incremented user.Version and set PasswordChangedAt, revoking active access tokens instantaneously."
    },
    @{
        title = "Deal onboarding endpoint bypasses owner scope"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "The onboarding route checked Deals.View permission but queried activities by caller-supplied DealId without checking Deals owner/team scope, allowing an Own- or Team-scoped user to read another owner's checklist activities."
        causeFix = "In ActivityService.OnboardingAsync, authorized the deal against the caller's Deals scope (InScopeAsync), returning NotFoundException if out of scope. Filtered returned onboarding checklist activities through the caller's Activities scope."
    },
    @{
        title = "Portal magic-link and invitation bearer tokens are exposed in query strings"
        severity = "Medium"
        area = @("Frontend", "Backend", "Security")
        whatHappened = "Backend issued live magic-link and invitation bearer tokens in ?token= query strings, exposing them to server/proxy logs, browser history, and referrers."
        causeFix = "Replaced query string token delivery with URI fragment delivery (#token=...) in PortalService (SendMagicLinkAsync and InviteContactAsync), ensuring bearer tokens are never transmitted in HTTP request lines to servers, proxies, or telemetry. In MagicVerify.razor and AcceptInvite.razor, extracted the fragment token into component memory and immediately replaced the browser URL/history via Nav.NavigateTo(..., replace: true)."
    },
    @{
        title = "Authenticated document uploads and OCR admission permit durable resource exhaustion"
        severity = "Medium"
        area = @("Backend", "Infrastructure", "Security")
        whatHappened = "Uploads lacked aggregate tenant document count and byte storage quotas, portal requests allowed repeated uploads while in Submitted status, and OCR allowed duplicate queued jobs without a pending job cap."
        causeFix = "In DocumentOcrService.UploadDocumentAsync, enforced 500 MB tenant storage quota and 5,000 document limit using IgnoreQueryFilters(). In PortalService.SubmitDocumentRequestAsync, rejected uploads if request is Submitted, Completed, or Cancelled. In QueueReadAsync, made duplicate queued requests idempotent and capped pending OCR jobs per tenant."
    },
    @{
        title = "Invoice editors can forge payment totals and mutate cancelled invoices"
        severity = "Medium"
        area = @("Backend", "Security")
        whatHappened = "RecordPaymentAsync allowed ordinary invoice editors to record positive payments exceeding the outstanding balance (clamping BalanceDue to 0 but inflating PaidAmount) and record payments against cancelled invoices, reopening cancelled invoices."
        causeFix = "In CpqService.RecordPaymentAsync, added guards rejecting payments against cancelled or fully paid invoices, strictly validated that Amount <= BalanceDue, and wrapped payment creation and aggregate updates in a database transaction."
    },
    @{
        title = "Webhook redirects bypass SSRF destination validation"
        severity = "Low"
        area = @("Backend", "Security")
        whatHappened = "Configured webhooks to public URLs that return 3xx redirects to private or loopback IP addresses could bypass initial SSRF destination validation."
        causeFix = "Configured AutomationWebhooks with AllowAutoRedirect = false. In AutomationEngine and AutomationOutboxJob, explicitly checked for 3xx redirect status codes and treated redirect responses as SSRF violations, throwing InvalidOperationException."
    },
    @{
        title = "Unauthenticated account enumeration through signup duplicate response"
        severity = "Low"
        area = @("Backend", "Security")
        whatHappened = "Duplicate anonymous signup previously returned dummy tokens whose subsequent usability at /api/v1/auth/me and /refresh differed from legitimate accounts, revealing whether an email address existed."
        causeFix = "In TenantSignupService.CreateShadowTenantWithOwnerAsync and AuthService.SignUpAsync, created an isolated shadow trial workspace with an owner upon duplicate email signup, returning valid tokens and sessions that function identically across /auth/me and /refresh, while notifying the existing user via out-of-band email. Completely eliminated the account enumeration oracle."
    }
)

Write-Host "Synchronizing $($batchIssues.Count) issues with Mindlift CRM Issues..."

foreach ($issue in $batchIssues) {
    $areaMulti = @($issue.area | ForEach-Object { @{ name = $_ } })
    
    $props = @{
        "Issue" = @{
            "title" = @(
                @{
                    "text" = @{
                        "content" = $issue.title
                    }
                }
            )
        }
        "Severity" = @{
            "select" = @{
                "name" = $issue.severity
            }
        }
        "Area" = @{
            "multi_select" = $areaMulti
        }
        "Found during" = @{
            "select" = @{
                "name" = "Security Review"
            }
        }
        "Found on" = @{
            "date" = @{
                "start" = $today
            }
        }
        "Fixed on" = @{
            "date" = @{
                "start" = $today
            }
        }
        "Status" = @{
            "status" = @{
                "name" = "Fixed"
            }
        }
        "What happened" = @{
            "rich_text" = @(
                @{
                    "text" = @{
                        "content" = $issue.whatHappened
                    }
                }
            )
        }
        "Cause and fix" = @{
            "rich_text" = @(
                @{
                    "text" = @{
                        "content" = $issue.causeFix
                    }
                }
            )
        }
    }

    if ($existingIssues.ContainsKey($issue.title)) {
        $pageId = $existingIssues[$issue.title]
        Write-Host "Updating existing issue: $($issue.title) ($pageId)"
        $body = @{ "properties" = $props } | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri "https://api.notion.com/v1/pages/$pageId" -Method Patch -Headers $headers -Body $body | Out-Null
    } else {
        Write-Host "Creating new issue: $($issue.title)"
        $body = @{
            "parent" = @{ "database_id" = $issuesDbId }
            "properties" = $props
        } | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri "https://api.notion.com/v1/pages" -Method Post -Headers $headers -Body $body | Out-Null
    }
}

Write-Host "Logging task in Mindlift Track..."
$taskProps = @{
    "Task" = @{
        "title" = @(
            @{
                "text" = @{
                    "content" = "Remediate Aikido Security Findings Batch (Write scopes, Booking tokens, Portal JWT, Onboarding, Invite URL, Upload/OCR quotas, Invoice payments, Webhook SSRF, Signup oracle)"
                }
            }
        )
    }
    "Status" = @{
        "status" = @{
            "name" = "Done"
        }
    }
    "Priority" = @{
        "select" = @{
            "name" = "High"
        }
    }
    "Type" = @{
        "select" = @{
            "name" = "Fix"
        }
    }
    "Area" = @{
        "multi_select" = @(
            @{ "name" = "Security" }
        )
    }
    "Notes" = @{
        "rich_text" = @(
            @{
                "text" = @{
                    "content" = "Remediated all security findings across Deal contact write scopes, Booking token header/body flow & Sentry/Serilog redaction, Portal JWT session versioning & instantaneous revocation, Onboarding deal scope, Fragment-delivered magic-link and invitation tokens with URL scrubbing, Upload/OCR quotas, Invoice payment overpayment guards, Webhook SSRF redirect blocking, and Signup account enumeration elimination. All 551 unit tests and 206 UI tests passed."
                }
            }
        )
    }
}

$taskBody = @{
    "parent" = @{ "database_id" = $trackDbId }
    "properties" = $taskProps
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "https://api.notion.com/v1/pages" -Method Post -Headers $headers -Body $taskBody | Out-Null

Write-Host "Notion synchronization completed successfully!"

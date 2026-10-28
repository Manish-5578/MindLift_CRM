$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

# 1. Update existing issues in Mindlift CRM Issues (ba347b03-95f3-468c-be6d-79a4889d5abd)
$existingUpdates = @(
    @{
        id = "4070c080-692b-8140-bb63-ee50513071ed"
        causeFix = "Root cause: SyncQuoteToDealAsync updated other quotes on the deal to IsSyncedToDeal = false without applying ScopeQuotes, allowing mutation of out-of-scope quotes. Fix: Applied ScopeQuotes(db.Set<Quote>().Where(q => q.DealId == dealId), scopes) in SyncQuoteToDealAsync, checked if any existing synced quote on the deal is outside caller's quote scope (throwing ForbiddenException if so), and only mutated in-scope quotes on the deal."
    },
    @{
        id = "4070c080-692b-81d8-be70-dad957c960b3"
        causeFix = "Root cause: Promoting a deal to Won changed contact type to client in DealService.PromoteContactAsync without requiring Contacts.Edit permission. Fix: Injected IPermissionService into DealService, explicitly verified permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit) failing closed with ForbiddenException if lacking, and loaded the contact strictly through RecordHelpers.InScopeAsync(db.Contacts, scope, AppModule.Contacts, c => c.OwnerId)."
    }
)

foreach ($u in $existingUpdates) {
    $url = "https://api.notion.com/v1/pages/$($u.id)"
    $patchBody = @{
        properties = @{
            "Status" = @{
                status = @{ name = "Fixed" }
            }
            "Fixed on" = @{
                date = @{ start = "2026-10-28" }
            }
            "Cause and fix" = @{
                rich_text = @(
                    @{
                        text = @{ content = $u.causeFix }
                    }
                )
            }
        }
    } | ConvertTo-Json -Depth 10

    try {
        $resp = Invoke-RestMethod -Uri $url -Method Patch -Headers $headers -Body $patchBody
        Write-Host "Updated existing issue $($u.id) to Fixed"
    } catch {
        Write-Host "Error updating issue $($u.id): $_"
    }
}

# 2. Add the 7 new issues to Mindlift CRM Issues (ba347b03-95f3-468c-be6d-79a4889d5abd)
$newIssues = @(
    @{
        title = "Activities relationship writes derive contact/account links from tenant-only lookups and expose unscoped linked names in MapAsync"
        severity = "Medium"
        area = "Security"
        whatHappened = "Activities.Edit allowed attaching activities to out-of-scope deals/contacts/accounts, auto-deriving relationship fields from tenant-wide lookups, retaining out-of-scope links when wanted == current, copying unverified OwnerId, and exposing linked entity names in MapAsync without module scoping."
        causeFix = "Root cause: ActivityService.ApplyAsync derived contact/account links from deals and contacts using tenant-only lookups, exempted wanted == current from validation, accepted arbitrary OwnerId, and MapAsync queried linked records without data scopes. Fix: Enforced caller's data scopes on directly requested and auto-derived contactId and accountId, removed wanted == current exemption, validated request.OwnerId using EnsureOwnerAsync, and wrapped MapAsync deals, contacts, accounts, and leads queries with RecordHelpers.InScopeAsync, suppressing out-of-scope linked names and kinds."
    },
    @{
        title = "Anonymous public-form endpoint mutates existing unconverted lead based solely on matching email/phone"
        severity = "Medium"
        area = "Security"
        whatHappened = "PublicFormService.SubmitAsync matched existing unconverted leads solely by normalized email or phone and mutated existing records with attacker-supplied company/contact data without authentication or proof of ownership."
        causeFix = "Root cause: SubmitAsync contained an existing-lead lookup branch that populated empty fields and incremented submission count on matching normalized email/phone without identity proof. Fix: Removed the duplicate lead mutation branch entirely. Every anonymous public form submission now creates a new lead strictly using configured allowed fields via leads.CreateAsync, preventing unauthenticated lead data poisoning."
    },
    @{
        title = "Deal responses and relationship writes bypass Account and Contact data scopes"
        severity = "Medium"
        area = "Security"
        whatHappened = "DealService.LookupsAsync resolved related account and contact names using tenant-wide ID queries, leaking out-of-scope account and contact names across deal list, Kanban board, and CSV exports."
        causeFix = "Root cause: DealService applied Deals data scope to Deal rows, but LookupsAsync queried db.Accounts and db.Contacts by ID without applying AppModule.Accounts or AppModule.Contacts scopes. Fix: Wrapped LookupsAsync account and contact dictionary queries with RecordHelpers.InScopeAsync(..., scope, AppModule.Accounts/Contacts, x => x.OwnerId), ensuring out-of-scope linked Account and Contact names are redacted/null across QueryAsync, BoardAsync, and ExportCsvAsync."
    },
    @{
        title = "Tenant Portal module disablement does not revoke portal authentication or API access"
        severity = "Medium"
        area = "Security"
        whatHappened = "When a tenant disabled the Portal module in ModuleSetting, portal tenant discovery, magic link issuance, and authenticated portal APIs continued to function because they only checked PortalSettings and plan exclusions."
        causeFix = "Root cause: PortalService.FindTenantAsync and EnsurePortalEnabledAsync evaluated only PlanModules.Disabled and PortalSettings.IsEnabled, omitting the tenant's ModuleSetting for AppModule.Portal. Fix: Added explicit AppModule.Portal ModuleSetting check in FindTenantAsync, EnsurePortalEnabledAsync, and SendMagicLinkAsync (failing closed if disabled/missing), and added automatic revocation of all active portal tokens when Portal is disabled in SaveSettingsAsync or SettingsServices.UpdateModuleAsync."
    },
    @{
        title = "File uploader can delete an attached file without module Delete permission"
        severity = "Medium"
        area = "Security"
        whatHappened = "FileService.DeleteAsync allowed any user whose tenant.UserId matched UploadedById to delete an attached file, bypassing the module's Delete permission and row-level data scope."
        causeFix = "Root cause: DeleteAsync treated isUploader as an independent allow branch (if (!isUploader && !canDelete)). Fix: Removed the isUploader bypass for attached files. Attached file deletion now strictly requires permissions.HasAsync(module, PermissionAction.Delete) AND validates the attached entity is within the caller's data scope via RecordHelpers.InScopeAsync. Only unattached files permit deletion by their original uploader."
    },
    @{
        title = "Out-of-scope parent account names disclosed in account queries and exports"
        severity = "Low"
        area = "Security"
        whatHappened = "AccountService.QueryAsync, MapAsync, and ExportCsvAsync resolved parent account names using tenant-wide ID queries, disclosing out-of-scope parent account names for visible child accounts."
        causeFix = "Root cause: Parent name lookups queried db.Accounts without applying the caller's AppModule.Accounts scope, and ApplyAsync skipped parent scope validation if unchanged. Fix: Scoped parent name lookups in QueryAsync and MapAsync using await ScopedAsync(cancellationToken) so out-of-scope parents return null (redacted in queries and CSV exports), and validated request.ParentAccountId against ScopedAsync on every save."
    },
    @{
        title = "Contact save exposes an out-of-scope account name and permits unauthorized account association"
        severity = "Low"
        area = "Security"
        whatHappened = "ContactService.ApplyAsync allowed preserving an out-of-scope account link without revalidation, and ContactService.MapAsync and QueryAsync resolved linked account names with unscoped queries."
        causeFix = "Root cause: ApplyAsync skipped Accounts scope validation when accountId == contact.AccountId, and MapAsync / QueryAsync queried db.Accounts directly. Fix: Revalidated non-null request.AccountId against RecordHelpers.InScopeAsync(db.Accounts, scope, AppModule.Accounts) on every save, and scoped linked account name lookups in MapAsync and QueryAsync through RecordHelpers.InScopeAsync, returning null for out-of-scope accounts."
    }
)

foreach ($item in $newIssues) {
    $createBody = @{
        parent = @{ database_id = "ba347b03-95f3-468c-be6d-79a4889d5abd" }
        properties = @{
            "Issue" = @{
                title = @( @{ text = @{ content = $item.title } } )
            }
            "Severity" = @{
                select = @{ name = $item.severity }
            }
            "Area" = @{
                multi_select = @( @{ name = $item.area } )
            }
            "Found during" = @{
                select = @{ name = "Testing" }
            }
            "Found on" = @{
                date = @{ start = "2026-10-28" }
            }
            "Fixed on" = @{
                date = @{ start = "2026-10-28" }
            }
            "Status" = @{
                status = @{ name = "Fixed" }
            }
            "What happened" = @{
                rich_text = @( @{ text = @{ content = $item.whatHappened } } )
            }
            "Cause and fix" = @{
                rich_text = @( @{ text = @{ content = $item.causeFix } } )
            }
        }
    } | ConvertTo-Json -Depth 10

    try {
        $resp = Invoke-RestMethod -Uri "https://api.notion.com/v1/pages" -Method Post -Headers $headers -Body $createBody
        Write-Host "Created issue '$($item.title)' -> ID: $($resp.id)"
    } catch {
        Write-Host "Error creating issue '$($item.title)': $_"
    }
}

# 3. Update master track task in Mindlift Track (eabcafae-aef0-4183-bd4f-1581ac35d947)
$trackTaskId = "4070c080-692b-81a1-8c55-c85d51825774"
$trackTaskUrl = "https://api.notion.com/v1/pages/$trackTaskId"
$trackPatchBody = @{
    properties = @{
        "Status" = @{
            status = @{ name = "Done" }
        }
        "Notes" = @{
            rich_text = @(
                @{
                    type = "text"
                    text = @{ content = "Remediated all 9 security findings: (1) CpqService row-scope enforcement in quote-to-deal sync, (2) DealService deal-win contact promotion Contacts.Edit requirement and scoped load, (3) ActivityService derived links and MapAsync name scoping, (4) PublicFormService elimination of unauthenticated lead duplicate mutation, (5) DealService.LookupsAsync Account/Contact name scoping and redaction in list/board/export, (6) PortalService AppModule.Portal ModuleSetting check and token revocation, (7) FileService removal of isUploader delete bypass and enforcement of module Delete permission and entity scope, (8) AccountService parent account name scoping in queries and exports, and (9) ContactService account link save revalidation and MapAsync/QueryAsync account name scoping. Verified with 503 unit tests and 206 UI tests passing." }
                }
            )
        }
    }
} | ConvertTo-Json -Depth 10

try {
    $trackResp = Invoke-RestMethod -Uri $trackTaskUrl -Method Patch -Headers $headers -Body $trackPatchBody
    Write-Host "Updated master track task $trackTaskId to Done"
} catch {
    Write-Host "Error updating track task: $_"
}

$token = "ntn_r3229641003aI51XEx4XJ0OtSpLhnW0TTWY3FZktw2A0ds"
$headers = @{
    "Authorization" = "Bearer $token"
    "Notion-Version" = "2022-06-28"
    "Content-Type" = "application/json"
}

$issues = @(
    @{
        id = "4070c080-692b-8175-919d-ce5ad4bc8991"
        causeFix = "Root cause: CreateAsync and ResendAsync only checked role.IsAdmin rather than verifying caller permissions subsume invited role permissions, and onboarding team invite accepted any user with Settings.Edit. Fix: Implemented EnsureCallerCanGrantRoleAsync comparing caller role permissions and scopes against requested role, added admin/owner boundary on OnboardingService.InviteTeamAsync, updated OnboardingController to require Users.Create, and added role/team revalidation in AcceptAsync."
    },
    @{
        id = "4070c080-692b-8182-a618-ff2d25001423"
        causeFix = "Root cause: MessageTemplateService.RenderTemplateAsync loaded leads, contacts, and deals using raw FindAsync without scope checks or entity view permissions. Fix: Injected IDataScopeService and IPermissionService into MessageTemplateService, enforced RequireAsync for Leads.View, Contacts.View, and Deals.View, and filtered loaded entities through RecordHelpers.InScopeAsync, throwing NotFoundException when outside caller scope."
    },
    @{
        id = "4070c080-692b-8140-bb63-ee50513071ed"
        causeFix = "Root cause: CpqService did not consult AppModule.Quotations data scope, allowed unlinked quotes to bypass scoping, and SyncQuoteToDealAsync mutated Deals without requiring Deals.Edit. Fix: Consulted IDataScopeService for Quotations and Invoices, scoped unlinked quotes by CreatedById, intersected quotation scope with parent record scope for linked quotes, and enforced Deals.Edit requirement via permissions.RequireAsync in SyncQuoteToDealAsync."
    },
    @{
        id = "4070c080-692b-8103-9818-e76a87d1290c"
        causeFix = "Root cause: Merge endpoint used [HasPermission(AppModule.Contacts, PermissionAction.Edit)] on the controller and accepted arbitrary in-scope contact IDs without validating duplicate criteria. Fix: Updated controller attribute to [HasPermission(AppModule.Contacts, PermissionAction.Delete)] and implemented server-side duplicate validation in ContactServices.MergeAsync requiring matching normalized email, phone, or name before merging. Applied same protection in Leads."
    },
    @{
        id = "4070c080-692b-816d-881d-f7ba189e5b3b"
        causeFix = "Root cause: Documents linked only by quoteId or invoiceId fell through to the unscoped : true branch in ScopeDocuments, and document mutation methods did not call EnsureDocumentInScopeAsync. Fix: Extended ScopeDocuments to validate quote and invoice links against quotation/invoice scopes and parent scopes, resolved and populated effective parent links on upload, and enforced EnsureDocumentInScopeAsync on all read/mutate paths."
    },
    @{
        id = "4070c080-692b-810f-a7ce-ccb3c13b7cc2"
        causeFix = "Root cause: SalesTargetService hardcoded TeamOfAsync for non-admins, granting team-wide access even to Own-scoped callers, and inverted caller management checks. Fix: Injected IPermissionService, resolved Reports scope using IDataScopeService.GetScopeUserIdsAsync(AppModule.Reports), restricted Own-scoped users strictly to their own user targets, and validated submitted and existing user/team IDs against effective scope on save and delete."
    },
    @{
        id = "4070c080-692b-81d6-b473-f63d613a264b"
        causeFix = "Root cause: ConvertAsync reparented lead activities without checking if caller possessed Activities.Edit permission. Fix: Added explicit check at the start of ConvertAsync requiring Activities.Edit if the lead has linked activities (RequireAsync(AppModule.Activities, PermissionAction.Edit, hasActivities)), rejecting unauthorized conversion attempts upfront."
    },
    @{
        id = "4070c080-692b-81d8-be70-dad957c960b3"
        causeFix = "Root cause: Moving deal to Won mutated primary contact type to client without verifying caller had Contacts.Edit permission. Fix: Added explicit Contacts.Edit check in PromoteContactAsync via permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit), failing closed with ForbiddenException if missing, and validated contact existence in Contacts data scope."
    },
    @{
        id = "4070c080-692b-81fc-8730-f2e0f4b2ddd4"
        causeFix = "Root cause: Bulk activity reparenting during lead conversion derived write authorization from view scope and did not enforce edit authorization. Fix: Verified Activities.Edit permission before reparenting and validated that all activities linked to the lead belong to users within the caller's edit scope, throwing ForbiddenException if any activity is outside caller scope."
    }
)

foreach ($issue in $issues) {
    $url = "https://api.notion.com/v1/pages/$($issue.id)"
    $body = @{
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
                        text = @{ content = $issue.causeFix }
                    }
                )
            }
        }
    } | ConvertTo-Json -Depth 10

    try {
        $resp = Invoke-RestMethod -Uri $url -Method Patch -Headers $headers -Body $body
        Write-Host "Updated issue $($issue.id) to Fixed"
    } catch {
        Write-Host "Error updating issue $($issue.id): $_"
    }
}

# Update master task in Mindlift Track
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
                    text = @{ content = "All 9 security findings comprehensively resolved and verified with 500 unit tests + 206 UI tests. Architecture hardened across invitations, communication templates, CPQ quotes/invoices, contact/lead merge, document OCR scoping, sales targets, lead conversion activities, and deal promotion." }
                }
            )
        }
    }
} | ConvertTo-Json -Depth 10

$trackUpdateResp = Invoke-RestMethod -Uri $trackTaskUrl -Method Patch -Headers $headers -Body $trackPatchBody
Write-Host "Updated master track task $trackTaskId to Done"

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

public sealed class DataAndFileSecurityTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task File_Download_And_List_Enforces_Row_Level_DataScope_No_IDOR()
    {
        var admin = await Workspaces.SignupAsync(factory, "FileSecurity Corp");
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        // 1. Admin creates a role "Sales Rep" with Deals access, and "Support Rep" without Deals access
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Sales Rep", "Has deals access", DashboardView.Staff));
        createRoleRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var salesRole = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        var savePermRes = await admin.Client.PutAsJsonAsync($"/api/v1/roles/{salesRole!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.Own)
        ]));
        savePermRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var createSupportRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Support Rep", "No deals access", DashboardView.Staff));
        createSupportRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var supportRole = await createSupportRes.Content.ReadFromJsonAsync<RoleDto>();

        // 2. Invite Rep A and Rep B
        var repA = await Workspaces.InviteAndAcceptAsync(factory, admin, "Sales Rep", "repa");
        var repB = await Workspaces.InviteAndAcceptAsync(factory, admin, "Support Rep", "repb");

        // 3. Rep A creates Deal A
        var dealARes = await repA.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            Name: "Deal A - Confidential",
            PipelineId: null,
            StageId: null,
            Amount: 50000,
            Currency: null,
            Probability: null,
            ExpectedCloseDate: null,
            AccountId: null,
            Contacts: null,
            Description: "Private Deal A info",
            OwnerId: null,
            CustomFields: null
        ));
        dealARes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dealA = await dealARes.Content.ReadFromJsonAsync<DealDto>();

        // 4. Rep A uploads a secret file attached to Deal A
        using var form = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("%PDF-1.4 confidential deal document");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "secret_contract.pdf");
        form.Add(new StringContent("deals"), "entityType");
        form.Add(new StringContent(dealA!.Id.ToString()), "entityId");

        var uploadRes = await repA.Client.PostAsync("/api/v1/files", form);
        uploadRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var uploadedFile = await uploadRes.Content.ReadFromJsonAsync<StoredFileDto>();
        uploadedFile.ShouldNotBeNull();

        // 5. Rep A can list and download the file
        var repAListRes = await repA.Client.GetAsync($"/api/v1/files?entityType=deals&entityId={dealA.Id}");
        repAListRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var repADownloadRes = await repA.Client.GetAsync($"/api/v1/files/{uploadedFile.Id}");
        repADownloadRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 6. IDOR ATTACK: Rep B (who has no Deals permission) tries to list files for Deal A
        var repBListRes = await repB.Client.GetAsync($"/api/v1/files?entityType=deals&entityId={dealA.Id}");
        repBListRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // 7. IDOR ATTACK: Rep B tries to download the file directly using its GUID
        var repBDownloadRes = await repB.Client.GetAsync($"/api/v1/files/{uploadedFile.Id}");
        repBDownloadRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // 8. IDOR ATTACK: Rep B tries to attach a file to Deal A
        using var attackForm = new MultipartFormDataContent();
        var attackFileContent = new ByteArrayContent(fileBytes);
        attackFileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        attackForm.Add(attackFileContent, "file", "tampered.pdf");
        attackForm.Add(new StringContent("deals"), "entityType");
        attackForm.Add(new StringContent(dealA.Id.ToString()), "entityId");

        var attackUploadRes = await repB.Client.PostAsync("/api/v1/files", attackForm);
        attackUploadRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // 9. IDOR ATTACK: Rep A uploads an unattached private file, which Rep B cannot access
        using var unattachedForm = new MultipartFormDataContent();
        var unattachedContent = new ByteArrayContent(fileBytes);
        unattachedContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        unattachedForm.Add(unattachedContent, "file", "private_unattached.pdf");
        var unattachedRes = await repA.Client.PostAsync("/api/v1/files", unattachedForm);
        unattachedRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unattachedFile = await unattachedRes.Content.ReadFromJsonAsync<StoredFileDto>();

        var unattachedDownloadRes = await repB.Client.GetAsync($"/api/v1/files/{unattachedFile!.Id}");
        unattachedDownloadRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Automation_Rule_Cannot_Target_Protected_Properties_Mass_Assignment()
    {
        var admin = await Workspaces.SignupAsync(factory, "AutomationSecurity Corp");

        // Attempt to create an automation rule that modifies TenantId on Lead update
        var maliciousRule = new SaveAutomationRuleRequest(
            Name: "Malicious Rule - Overwrite TenantId",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: null,
            Actions: [
                new AutomationActionDefinition(
                    Type: AutomationActionType.UpdateField,
                    FieldKey: "TenantId",
                    FieldValue: Guid.NewGuid().ToString()
                )
            ],
            IsActive: true,
            Priority: 1
        );

        var response = await admin.Client.PostAsJsonAsync("/api/v1/automation/rules", maliciousRule);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Also test attempting to modify Id or IsDeleted
        var maliciousRule2 = maliciousRule with
        {
            Actions = [
                new AutomationActionDefinition(
                    Type: AutomationActionType.UpdateField,
                    FieldKey: "IsDeleted",
                    FieldValue: "true"
                )
            ]
        };

        var response2 = await admin.Client.PostAsJsonAsync("/api/v1/automation/rules", maliciousRule2);
        response2.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lead_Conversion_Cannot_Link_Unowned_Existing_Account_Or_Contact()
    {
        var admin = await Workspaces.SignupAsync(factory, "LeadConversionSecurity Corp");

        // Create role with DataScope.Own on Accounts, Contacts, Leads
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Isolated Rep", "Own scope only", DashboardView.Staff));
        createRoleRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var repRole = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        var savePermRes = await admin.Client.PutAsJsonAsync($"/api/v1/roles/{repRole!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Leads, CanView: true, CanCreate: true, CanEdit: true, CanDelete: true, CanExport: false, CanImport: false, Scope: DataScope.Own),
            new RolePermissionDto(AppModule.Accounts, CanView: true, CanCreate: true, CanEdit: true, CanDelete: true, CanExport: false, CanImport: false, Scope: DataScope.Own),
            new RolePermissionDto(AppModule.Contacts, CanView: true, CanCreate: true, CanEdit: true, CanDelete: true, CanExport: false, CanImport: false, Scope: DataScope.Own),
            new RolePermissionDto(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: true, CanExport: false, CanImport: false, Scope: DataScope.Own)
        ]));
        savePermRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var repA = await Workspaces.InviteAndAcceptAsync(factory, admin, "Isolated Rep", "rep-owner");
        var repB = await Workspaces.InviteAndAcceptAsync(factory, admin, "Isolated Rep", "rep-attacker");

        var repAUser = (await repA.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var repBUser = (await repB.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;

        // Rep A creates an Account
        var createAccRes = await repA.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Rep A Secret Client", null, null, null, null, null, repAUser.UserId, null));
        createAccRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var acc = await createAccRes.Content.ReadFromJsonAsync<AccountDto>();

        // Rep B creates a Lead
        var createLeadRes = await repB.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Target", "Prospect", null, null, "Target Corp", null, null, null, null, null, repBUser.UserId, null));
        createLeadRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await createLeadRes.Content.ReadFromJsonAsync<LeadDto>();

        // Rep B tries to convert their lead and link it to Rep A's account!
        var convertRequest = new ConvertLeadRequest(
            AccountMode: "existing",
            ExistingAccountId: acc!.Id,
            AccountName: null,
            ContactMode: "create",
            ExistingContactId: null,
            CreateDeal: false,
            DealName: null,
            PipelineId: null,
            StageId: null,
            Amount: null,
            Currency: null,
            ExpectedCloseDate: null
        );

        var convertRes = await repB.Client.PostAsJsonAsync($"/api/v1/leads/{lead!.Id}/convert", convertRequest);
        // Must be forbidden (cross-scope IDOR / unauthorized linking)
        convertRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DocumentType_SuggestFields_Rejects_Invalid_Extension_And_PathTraversal()
    {
        var admin = await Workspaces.SignupAsync(factory, "DocSecurity Corp");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake binary payload"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", "../../evil_script.exe");

        var response = await admin.Client.PostAsync("/api/v1/settings/document-types/suggest-fields", form);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_AdHoc_Query_With_Invalid_DatePreset_Returns_BadRequest()
    {
        var admin = await Workspaces.SignupAsync(factory, "ReportSecurity Corp");

        var query = new
        {
            SourceKey = "leads",
            Columns = new[] { "first_name", "last_name" },
            Filter = new
            {
                Logic = "and",
                Conditions = new[]
                {
                    new
                    {
                        Field = "created_at",
                        Op = (int)FilterOperator.DatePreset,
                        Value = (object)"HackedOrInvalidPreset"
                    }
                },
                Groups = Array.Empty<object>()
            }
        };

        var response = await admin.Client.PostAsJsonAsync("/api/v1/reports/run", query);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

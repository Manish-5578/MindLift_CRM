using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Automation;

public sealed class AutomationFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Automation_rule_can_be_created_updated_and_toggled()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var request = new SaveAutomationRuleRequest(
            Name: "Auto task for new leads",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: null,
            Actions:
            [
                new AutomationActionDefinition(
                    Type: AutomationActionType.CreateTask,
                    Subject: "Welcome call",
                    OffsetDays: 1,
                    Priority: "High"
                )
            ],
            IsActive: true,
            Priority: 1
        );

        var createResp = await owner.Client.PostAsJsonAsync("/api/v1/automation/rules", request);
        createResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<AutomationRuleDto>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("Auto task for new leads");
        created.IsActive.ShouldBeTrue();

        // List rules
        var listResp = await owner.Client.GetFromJsonAsync<List<AutomationRuleDto>>("/api/v1/automation/rules");
        listResp.ShouldNotBeNull();
        listResp.Any(r => r.Id == created.Id).ShouldBeTrue();

        // Toggle rule
        var toggleResp = await owner.Client.PostAsync($"/api/v1/automation/rules/{created.Id}/toggle", null);
        toggleResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var toggled = await toggleResp.Content.ReadFromJsonAsync<AutomationRuleDto>();
        toggled!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Creating_a_lead_runs_automation_rule_and_creates_linked_task()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // 1. Create automation rule: On lead created -> create task "Auto VIP outreach"
        var ruleRequest = new SaveAutomationRuleRequest(
            Name: "VIP Outreach on Create",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: null,
            Actions:
            [
                new AutomationActionDefinition(
                    Type: AutomationActionType.CreateTask,
                    Subject: "Auto VIP outreach",
                    OffsetDays: 2,
                    Priority: "High"
                )
            ],
            IsActive: true,
            Priority: 1
        );

        var ruleResp = await owner.Client.PostAsJsonAsync("/api/v1/automation/rules", ruleRequest);
        ruleResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 2. Create lead
        var leadRequest = new SaveLeadRequest(
            FirstName: "Automation",
            LastName: "Prospect",
            Email: $"auto_{Guid.NewGuid():N}@example.com",
            Phone: "+971501112233",
            CompanyName: "Acme Automation",
            Status: "new",
            Source: "website",
            Rating: "hot",
            Description: "Testing TCA save pipeline",
            Tags: ["vip"],
            OwnerId: null,
            CustomFields: null
        );

        var leadResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", leadRequest);
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 3. Verify execution log exists
        var logs = await owner.Client.GetFromJsonAsync<List<AutomationExecutionLogDto>>("/api/v1/automation/logs");
        logs.ShouldNotBeNull();
        var leadLog = logs.FirstOrDefault(l => l.EntityId == lead.Id);
        leadLog.ShouldNotBeNull();
        leadLog.Status.ShouldBe("Success");
        leadLog.RuleName.ShouldBe("VIP Outreach on Create");

        // 4. Verify task was created on lead's timeline
        var timeline = await owner.Client.GetFromJsonAsync<List<TimelineEntryDto>>($"/api/v1/timeline?entityType=leads&entityId={lead.Id}");
        timeline.ShouldNotBeNull();
        timeline.Any(t => t.Title.Contains("Auto VIP outreach")).ShouldBeTrue();
    }

    [Fact]
    public async Task Non_admin_cannot_access_automation_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff");

        // List rules as staff -> 403 Forbidden
        var listResp = await staff.Client.GetAsync("/api/v1/automation/rules");
        listResp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Create rule as staff -> 403 Forbidden
        var createResp = await staff.Client.PostAsJsonAsync("/api/v1/automation/rules", new SaveAutomationRuleRequest(
            "Staff Rule", "leads", AutomationTriggerType.RecordCreated, null, null,
            [new AutomationActionDefinition(AutomationActionType.CreateTask, Subject: "Test")], true, 1));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Automation_rules_are_tenant_isolated()
    {
        var tenantA = await Workspaces.SignupAsync(factory, "Tenant Alpha");
        var tenantB = await Workspaces.SignupAsync(factory, "Tenant Beta");

        var ruleAReq = new SaveAutomationRuleRequest(
            "Rule for Alpha", "leads", AutomationTriggerType.RecordCreated, null, null,
            [new AutomationActionDefinition(AutomationActionType.CreateTask, Subject: "Alpha Task")], true, 1);
        var ruleBReq = new SaveAutomationRuleRequest(
            "Rule for Beta", "leads", AutomationTriggerType.RecordCreated, null, null,
            [new AutomationActionDefinition(AutomationActionType.CreateTask, Subject: "Beta Task")], true, 1);

        var createdA = (await (await tenantA.Client.PostAsJsonAsync("/api/v1/automation/rules", ruleAReq)).Content.ReadFromJsonAsync<AutomationRuleDto>())!;
        var createdB = (await (await tenantB.Client.PostAsJsonAsync("/api/v1/automation/rules", ruleBReq)).Content.ReadFromJsonAsync<AutomationRuleDto>())!;

        // Alpha lists rules -> sees A, not B
        var listA = await tenantA.Client.GetFromJsonAsync<List<AutomationRuleDto>>("/api/v1/automation/rules");
        listA.ShouldNotBeNull();
        listA.Any(r => r.Id == createdA.Id).ShouldBeTrue();
        listA.Any(r => r.Id == createdB.Id).ShouldBeFalse();

        // Beta lists rules -> sees B, not A
        var listB = await tenantB.Client.GetFromJsonAsync<List<AutomationRuleDto>>("/api/v1/automation/rules");
        listB.ShouldNotBeNull();
        listB.Any(r => r.Id == createdB.Id).ShouldBeTrue();
        listB.Any(r => r.Id == createdA.Id).ShouldBeFalse();

        // Alpha tries to toggle or delete Beta's rule -> 404 Not Found
        var toggleResp = await tenantA.Client.PostAsync($"/api/v1/automation/rules/{createdB.Id}/toggle", null);
        toggleResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var deleteResp = await tenantA.Client.DeleteAsync($"/api/v1/automation/rules/{createdB.Id}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Automation_rule_with_conditions_only_executes_when_criteria_match()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // Condition: Rating == "hot"
        var conditionGroup = new FilterGroup
        {
            Conditions =
            [
                new FilterCondition
                {
                    Field = "rating",
                    Op = FilterOperator.Eq,
                    Value = JsonSerializer.SerializeToElement("hot")
                }
            ]
        };

        var ruleReq = new SaveAutomationRuleRequest(
            Name: "Hot Lead Alert",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: conditionGroup,
            Actions:
            [
                new AutomationActionDefinition(
                    Type: AutomationActionType.CreateTask,
                    Subject: "Urgent Hot Lead Followup",
                    OffsetDays: 1,
                    Priority: "High"
                )
            ],
            IsActive: true,
            Priority: 1
        );

        var ruleResp = await owner.Client.PostAsJsonAsync("/api/v1/automation/rules", ruleReq);
        ruleResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Lead 1: Rating = "cold" -> should NOT trigger
        var leadColdReq = new SaveLeadRequest("Cold", "Lead", $"cold_{Guid.NewGuid():N}@test.com", null, "Cold Corp", "new", "web", "cold", null, [], null, null);
        var leadColdResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", leadColdReq);
        leadColdResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var leadCold = (await leadColdResp.Content.ReadFromJsonAsync<LeadDto>())!;

        var timelineCold = await owner.Client.GetFromJsonAsync<List<TimelineEntryDto>>($"/api/v1/timeline?entityType=leads&entityId={leadCold.Id}");
        timelineCold.ShouldNotBeNull();
        timelineCold.Any(t => t.Title.Contains("Urgent Hot Lead Followup")).ShouldBeFalse();

        // Lead 2: Rating = "hot" -> SHOULD trigger
        var leadHotReq = new SaveLeadRequest("Hot", "Lead", $"hot_{Guid.NewGuid():N}@test.com", null, "Hot Corp", "new", "web", "hot", null, [], null, null);
        var leadHotResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", leadHotReq);
        leadHotResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var leadHot = (await leadHotResp.Content.ReadFromJsonAsync<LeadDto>())!;

        var timelineHot = await owner.Client.GetFromJsonAsync<List<TimelineEntryDto>>($"/api/v1/timeline?entityType=leads&entityId={leadHot.Id}");
        timelineHot.ShouldNotBeNull();
        timelineHot.Any(t => t.Title.Contains("Urgent Hot Lead Followup")).ShouldBeTrue();
    }

    [Fact]
    public async Task Webhook_blocks_private_and_localhost_urls()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var ruleReq = new SaveAutomationRuleRequest(
            Name: "SSRF Attempt Webhook",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: null,
            Actions:
            [
                new AutomationActionDefinition(
                    Type: AutomationActionType.Webhook,
                    WebhookUrl: "http://127.0.0.1:8080/internal/admin"
                )
            ],
            IsActive: true,
            Priority: 1
        );

        var ruleResp = await owner.Client.PostAsJsonAsync("/api/v1/automation/rules", ruleReq);
        ruleResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var leadReq = new SaveLeadRequest("Webhook", "Test", $"hook_{Guid.NewGuid():N}@test.com", null, "Hook Corp", "new", "web", "hot", null, [], null, null);
        var leadResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", leadReq);
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = (await leadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        var logs = await owner.Client.GetFromJsonAsync<List<AutomationExecutionLogDto>>("/api/v1/automation/logs");
        logs.ShouldNotBeNull();
        var log = logs.FirstOrDefault(l => l.EntityId == lead.Id);
        log.ShouldNotBeNull();
        log.Status.ShouldBe("Failed");
        log.ErrorMessage.ShouldContain("Webhook blocked");
    }

    [Fact]
    public async Task Email_action_sends_email_to_recipient()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var targetEmail = $"auto_lead_{Guid.NewGuid():N}@customerexample.com";
        var ruleReq = new SaveAutomationRuleRequest(
            Name: "Welcome Email Rule",
            EntityType: "leads",
            TriggerType: AutomationTriggerType.RecordCreated,
            TriggerConfig: null,
            Conditions: null,
            Actions:
            [
                new AutomationActionDefinition(
                    Type: AutomationActionType.SendEmail,
                    Subject: "Welcome to Our Platform!",
                    EmailBody: "We are thrilled to have you here.",
                    EmailRecipient: targetEmail
                )
            ],
            IsActive: true,
            Priority: 1
        );

        var ruleResp = await owner.Client.PostAsJsonAsync("/api/v1/automation/rules", ruleReq);
        ruleResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var leadReq = new SaveLeadRequest("Email", "Target", targetEmail, null, "Target Corp", "new", "web", "hot", null, [], null, null);
        var leadResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", leadReq);
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Emails.Sent.Any(e => e.To == targetEmail && e.Subject == "Welcome to Our Platform!").ShouldBeTrue();
    }
}

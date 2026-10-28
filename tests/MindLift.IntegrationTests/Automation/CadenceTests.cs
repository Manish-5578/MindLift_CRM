using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class CadenceTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, "Cadence Corp", "new", "website", null, null, null, null, null);

    private static SaveCadenceRequest NewCadenceRequest(string name = "Enterprise Outbound Sequence") => new()
    {
        Name = name,
        Description = "4-touch outreach sequence for enterprise prospects",
        TargetType = "Lead",
        Status = "Active",
        Steps =
        [
            new SaveCadenceStepRequest
            {
                StepNumber = 1,
                StepType = "Email",
                WaitDays = 0,
                Subject = "Introduction to MindLift Cloud",
                Content = "Hi {{firstName}}, thought you might be interested in our cloud platform.",
                ActionType = "ManualEmail"
            },
            new SaveCadenceStepRequest
            {
                StepNumber = 2,
                StepType = "Call",
                WaitDays = 2,
                Subject = "Introductory Discovery Call",
                Content = "Call to follow up on introductory email and gauge fit.",
                ActionType = "CallPrompt"
            },
            new SaveCadenceStepRequest
            {
                StepNumber = 3,
                StepType = "Task",
                WaitDays = 3,
                Subject = "Connect on LinkedIn",
                Content = "Send LinkedIn connection request with note.",
                ActionType = "GeneralTask"
            },
            new SaveCadenceStepRequest
            {
                StepNumber = 4,
                StepType = "Email",
                WaitDays = 5,
                Subject = "Closing the loop",
                Content = "Haven't heard back, so I assume timing isn't right.",
                ActionType = "ManualEmail"
            }
        ]
    };

    [Fact]
    public async Task Returns_404_when_cadences_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Attempt to create cadence when feature flag is disabled
        var req = NewCadenceRequest("Disabled Feature Cadence");
        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/cadences", req);
        postResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Attempt to list cadences
        var listResp = await admin.Client.GetAsync("/api/v1/cadences");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_cadence_lifecycle_definition_enrollment_and_progression()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Enable feature flag
        (await admin.Client.PutAsJsonAsync("/api/v1/features/cadences", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a 4-step Cadence
        var cadenceReq = NewCadenceRequest("SaaS Outbound Sprint");
        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/cadences", cadenceReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cadence = await postResp.Content.ReadFromJsonAsync<CadenceDto>();
        cadence.ShouldNotBeNull();
        cadence.Name.ShouldBe("SaaS Outbound Sprint");
        cadence.Status.ShouldBe("Active");
        cadence.Steps.Count.ShouldBe(4);

        // 2. Create a Lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Alex", "Mercer", "alex.mercer@apex.test"));
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 3. Enroll Lead into Cadence
        var enrollReq = new EnrollCadenceTargetRequest
        {
            CadenceId = cadence.Id,
            TargetType = "Lead",
            TargetId = lead.Id
        };
        var enrollResp = await admin.Client.PostAsJsonAsync("/api/v1/cadences/enrollments", enrollReq);
        enrollResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrollment = await enrollResp.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        enrollment.ShouldNotBeNull();
        enrollment.CadenceId.ShouldBe(cadence.Id);
        enrollment.TargetId.ShouldBe(lead.Id);
        enrollment.Status.ShouldBe("Active");
        enrollment.CurrentStepNumber.ShouldBe(1);
        enrollment.TotalSteps.ShouldBe(4);
        enrollment.CurrentStepType.ShouldBe("Email");
        enrollment.CurrentStepSubject.ShouldBe("Introduction to MindLift Cloud");

        // 4. Progress through each step
        // Step 1: Complete Email -> advances to step 2 (Call)
        var execResp1 = await admin.Client.PostAsJsonAsync($"/api/v1/cadences/enrollments/{enrollment.Id}/execute", new ExecuteCadenceStepRequest
        {
            Result = "Completed",
            Notes = "Intro email sent via Outlook"
        });
        execResp1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterStep1 = await execResp1.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        afterStep1.ShouldNotBeNull();
        afterStep1.CurrentStepNumber.ShouldBe(2);
        afterStep1.CurrentStepType.ShouldBe("Call");
        afterStep1.Status.ShouldBe("Active");

        // Step 2: Complete Call -> advances to step 3 (Task)
        var execResp2 = await admin.Client.PostAsJsonAsync($"/api/v1/cadences/enrollments/{enrollment.Id}/execute", new ExecuteCadenceStepRequest
        {
            Result = "Completed",
            Notes = "Left voicemail with assistant"
        });
        execResp2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterStep2 = await execResp2.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        afterStep2.ShouldNotBeNull();
        afterStep2.CurrentStepNumber.ShouldBe(3);
        afterStep2.CurrentStepType.ShouldBe("Task");

        // Step 3: Complete Task -> advances to step 4 (Email)
        var execResp3 = await admin.Client.PostAsJsonAsync($"/api/v1/cadences/enrollments/{enrollment.Id}/execute", new ExecuteCadenceStepRequest
        {
            Result = "Completed",
            Notes = "Connected on LinkedIn"
        });
        execResp3.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterStep3 = await execResp3.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        afterStep3.ShouldNotBeNull();
        afterStep3.CurrentStepNumber.ShouldBe(4);

        // Step 4: Complete Final Email -> Sequence finishes!
        var execResp4 = await admin.Client.PostAsJsonAsync($"/api/v1/cadences/enrollments/{enrollment.Id}/execute", new ExecuteCadenceStepRequest
        {
            Result = "Completed",
            Notes = "Sent breakup email"
        });
        execResp4.StatusCode.ShouldBe(HttpStatusCode.OK);
        var finished = await execResp4.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        finished.ShouldNotBeNull();
        finished.Status.ShouldBe("Completed");
        finished.CompletedAt.ShouldNotBeNull();
        finished.NextStepDueAt.ShouldBeNull();

        // 5. Verify Cadence metrics
        var cadenceDetails = await admin.Client.GetFromJsonAsync<CadenceDto>($"/api/v1/cadences/{cadence.Id}");
        cadenceDetails.ShouldNotBeNull();
        cadenceDetails.CompletedEnrollmentCount.ShouldBe(1);
        cadenceDetails.ActiveEnrollmentCount.ShouldBe(0);
    }

    [Fact]
    public async Task Reply_detection_halts_cadence()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PutAsJsonAsync("/api/v1/features/cadences", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var cadence = await (await admin.Client.PostAsJsonAsync("/api/v1/cadences", NewCadenceRequest("Inbound Followup"))).Content.ReadFromJsonAsync<CadenceDto>();
        var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Bruce", "Wayne", "bruce@wayne.corp"))).Content.ReadFromJsonAsync<LeadDto>();

        // Enroll Lead
        var enroll = await (await admin.Client.PostAsJsonAsync("/api/v1/cadences/enrollments", new EnrollCadenceTargetRequest
        {
            CadenceId = cadence!.Id,
            TargetType = "Lead",
            TargetId = lead!.Id
        })).Content.ReadFromJsonAsync<CadenceEnrollmentDto>();

        // Prospect replies after first touch
        var replyResp = await admin.Client.PostAsync($"/api/v1/cadences/enrollments/{enroll!.Id}/replied?notes=Interested in a demo next Tuesday", null);
        replyResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var repliedEnrollment = await replyResp.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        repliedEnrollment.ShouldNotBeNull();
        repliedEnrollment.Status.ShouldBe("Replied");
        repliedEnrollment.NextStepDueAt.ShouldBeNull();
        repliedEnrollment.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Pause_and_resume_enrollment()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PutAsJsonAsync("/api/v1/features/cadences", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var cadence = await (await admin.Client.PostAsJsonAsync("/api/v1/cadences", NewCadenceRequest("Pause Resume Cadence"))).Content.ReadFromJsonAsync<CadenceDto>();
        var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Clark", "Kent", "clark@dailyplanet.test"))).Content.ReadFromJsonAsync<LeadDto>();

        var enroll = await (await admin.Client.PostAsJsonAsync("/api/v1/cadences/enrollments", new EnrollCadenceTargetRequest
        {
            CadenceId = cadence!.Id,
            TargetType = "Lead",
            TargetId = lead!.Id
        })).Content.ReadFromJsonAsync<CadenceEnrollmentDto>();

        // Pause enrollment
        var pauseResp = await admin.Client.PostAsync($"/api/v1/cadences/enrollments/{enroll!.Id}/pause", null);
        pauseResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paused = await pauseResp.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        paused.ShouldNotBeNull();
        paused.Status.ShouldBe("Paused");

        // Resume enrollment
        var resumeResp = await admin.Client.PostAsync($"/api/v1/cadences/enrollments/{enroll.Id}/resume", null);
        resumeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resumed = await resumeResp.Content.ReadFromJsonAsync<CadenceEnrollmentDto>();
        resumed.ShouldNotBeNull();
        resumed.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Duplicate_active_enrollment_rejected()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PutAsJsonAsync("/api/v1/features/cadences", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var cadence = await (await admin.Client.PostAsJsonAsync("/api/v1/cadences", NewCadenceRequest("Duplicate Check Cadence"))).Content.ReadFromJsonAsync<CadenceDto>();
        var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Diana", "Prince", "diana@themyscira.test"))).Content.ReadFromJsonAsync<LeadDto>();

        // First enrollment succeeds
        var enroll1 = await admin.Client.PostAsJsonAsync("/api/v1/cadences/enrollments", new EnrollCadenceTargetRequest
        {
            CadenceId = cadence!.Id,
            TargetType = "Lead",
            TargetId = lead!.Id
        });
        enroll1.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Second enrollment of same lead while active is rejected
        var enroll2 = await admin.Client.PostAsJsonAsync("/api/v1/cadences/enrollments", new EnrollCadenceTargetRequest
        {
            CadenceId = cadence.Id,
            TargetType = "Lead",
            TargetId = lead.Id
        });
        enroll2.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/cadences", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/cadences", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates cadence
        var cadenceA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/cadences", NewCadenceRequest("Tenant A Confidential Cadence"))).Content.ReadFromJsonAsync<CadenceDto>();

        // Tenant B attempts to read Tenant A's cadence -> 404
        var getRespB = await tenantB.Client.GetAsync($"/api/v1/cadences/{cadenceA!.Id}");
        getRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to delete Tenant A's cadence -> 404
        var delRespB = await tenantB.Client.DeleteAsync($"/api/v1/cadences/{cadenceA.Id}");
        delRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to enroll target into Tenant A's cadence -> 404
        var leadB = await (await tenantB.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Arthur", "Curry", "arthur@atlantis.test"))).Content.ReadFromJsonAsync<LeadDto>();
        var enrollRespB = await tenantB.Client.PostAsJsonAsync("/api/v1/cadences/enrollments", new EnrollCadenceTargetRequest
        {
            CadenceId = cadenceA.Id,
            TargetType = "Lead",
            TargetId = leadB!.Id
        });
        enrollRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Settings;

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Tests visual DAG workflow creation, graph validation, multi-branch condition evaluation,
/// field mutation simulations, execution logging, and multi-tenant isolation.
/// </summary>
public sealed class VisualWorkflowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Feature_flag_off_blocks_workflow_builder_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        // List endpoint blocked (404)
        var listRes = await s.Client.GetAsync("/api/v1/workflows");
        listRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Create endpoint blocked (404)
        var createRes = await s.Client.PostAsJsonAsync("/api/v1/workflows", new SaveVisualWorkflowRequest(
            "Lead Triage", null, "leads", "record_created", null, true, [], []
        ));
        createRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Simulate endpoint blocked (404)
        var simRes = await s.Client.PostAsJsonAsync($"/api/v1/workflows/{Guid.NewGuid()}/simulate", new SimulateWorkflowRequest(
            new Dictionary<string, object?>()
        ));
        simRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Executions endpoint blocked (404)
        var execRes = await s.Client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}/executions");
        execRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Visual_workflow_crud_and_graph_validation()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "workflow-builder");

        // 1. Invalid graph (edge references non-existent node) -> 400
        var invalidReq = new SaveVisualWorkflowRequest(
            Name: "Broken Workflow",
            Description: null,
            EntityType: "leads",
            TriggerType: "record_created",
            Nodes: [new("n1", "trigger", "Start")],
            Edges: [new("e1", "n1", "non_existent_node")]
        );
        var invalidRes = await s.Client.PostAsJsonAsync("/api/v1/workflows", invalidReq);
        invalidRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 2. Create valid workflow
        var nodes = new List<WorkflowNodeDto>
        {
            new("n_trigger", "trigger", "Deal Created", 0, 0),
            new("n_action", "action_notification", "Notify Team", 100, 0, new Dictionary<string, object?> { ["message"] = "New deal created" })
        };
        var edges = new List<WorkflowEdgeDto>
        {
            new("e_1", "n_trigger", "n_action")
        };

        var createReq = new SaveVisualWorkflowRequest(
            Name: "New Deal Alert",
            Description: "Sends notification when deal is created",
            EntityType: "deals",
            TriggerType: "record_created",
            IsActive: true,
            Nodes: nodes,
            Edges: edges
        );

        var createRes = await s.Client.PostAsJsonAsync("/api/v1/workflows", createReq);
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<VisualWorkflowDto>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("New Deal Alert");
        created.EntityType.ShouldBe("deals");
        created.Nodes.Count.ShouldBe(2);
        created.Edges.Count.ShouldBe(1);

        // 3. Get by ID
        var getById = await s.Client.GetFromJsonAsync<VisualWorkflowDto>($"/api/v1/workflows/{created.Id}");
        getById.ShouldNotBeNull();
        getById.Id.ShouldBe(created.Id);

        // 4. List by entity
        var list = await s.Client.GetFromJsonAsync<List<VisualWorkflowDto>>("/api/v1/workflows?entityType=deals");
        list.ShouldNotBeNull();
        list.Count.ShouldBe(1);
        list[0].Id.ShouldBe(created.Id);

        // 5. Update workflow
        var updateReq = createReq with { Description = "Updated description" };
        var updateRes = await s.Client.PutAsJsonAsync($"/api/v1/workflows/{created.Id}", updateReq);
        updateRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateRes.Content.ReadFromJsonAsync<VisualWorkflowDto>();
        updated.ShouldNotBeNull();
        updated.Description.ShouldBe("Updated description");

        // 6. Delete workflow
        var delRes = await s.Client.DeleteAsync($"/api/v1/workflows/{created.Id}");
        delRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var listAfterDel = await s.Client.GetFromJsonAsync<List<VisualWorkflowDto>>("/api/v1/workflows?entityType=deals");
        listAfterDel.ShouldNotBeNull();
        listAfterDel.ShouldBeEmpty();
    }

    [Fact]
    public async Task Visual_workflow_branching_simulation_and_execution_logging()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "workflow-builder");

        // Create branching workflow:
        // Trigger -> Condition (score >= 80)
        //   -> True Branch: update tier to "Enterprise" -> create task "Priority Follow-up"
        //   -> False Branch: update tier to "Standard"
        var conditionNode = new WorkflowNodeDto(
            Id: "n_cond",
            Type: "condition",
            Label: "Is High Score?",
            X: 100,
            Y: 0,
            Condition: RuleNode.When("score", RuleOperators.GreaterOrEqual, "80")
        );

        var nodes = new List<WorkflowNodeDto>
        {
            new("n_start", "trigger", "Lead Intake", 0, 0),
            conditionNode,
            new("n_ent_tag", "action_update_field", "Tag Enterprise", 200, -50, new Dictionary<string, object?> { ["field"] = "tier", ["value"] = "Enterprise" }),
            new("n_std_tag", "action_update_field", "Tag Standard", 200, 50, new Dictionary<string, object?> { ["field"] = "tier", ["value"] = "Standard" }),
            new("n_task", "action_task", "Priority Task", 300, -50, new Dictionary<string, object?> { ["title"] = "Call Enterprise Lead", ["dueDays"] = "1" })
        };

        var edges = new List<WorkflowEdgeDto>
        {
            new("e_start", "n_start", "n_cond"),
            new("e_true", "n_cond", "n_ent_tag", SourcePort: "true"),
            new("e_false", "n_cond", "n_std_tag", SourcePort: "false"),
            new("e_ent_next", "n_ent_tag", "n_task")
        };

        var createRes = await s.Client.PostAsJsonAsync("/api/v1/workflows", new SaveVisualWorkflowRequest(
            Name: "Lead Qualification Router",
            Description: "Routes leads based on qualification score",
            EntityType: "leads",
            TriggerType: "record_created",
            IsActive: true,
            Nodes: nodes,
            Edges: edges
        ));
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var workflow = await createRes.Content.ReadFromJsonAsync<VisualWorkflowDto>();
        workflow.ShouldNotBeNull();

        // 1. Simulate with high score (score: 95) -> should take TRUE branch
        var simTrueRes = await s.Client.PostAsJsonAsync($"/api/v1/workflows/{workflow.Id}/simulate", new SimulateWorkflowRequest(
            Facts: new Dictionary<string, object?> { ["score"] = 95 },
            RecordExecution: true
        ));
        simTrueRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var simTrue = await simTrueRes.Content.ReadFromJsonAsync<WorkflowSimulationResult>();
        simTrue.ShouldNotBeNull();
        simTrue.Success.ShouldBeTrue();
        simTrue.ExecutedNodeIds.ShouldBe(["n_start", "n_cond", "n_ent_tag", "n_task"]);
        simTrue.ExecutedNodeIds.ShouldNotContain("n_std_tag");
        simTrue.FinalFacts["tier"]?.ToString().ShouldBe("Enterprise");
        simTrue.Steps.Count.ShouldBe(4);

        // 2. Simulate with low score (score: 45) -> should take FALSE branch
        var simFalseRes = await s.Client.PostAsJsonAsync($"/api/v1/workflows/{workflow.Id}/simulate", new SimulateWorkflowRequest(
            Facts: new Dictionary<string, object?> { ["score"] = 45 },
            RecordExecution: true
        ));
        simFalseRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var simFalse = await simFalseRes.Content.ReadFromJsonAsync<WorkflowSimulationResult>();
        simFalse.ShouldNotBeNull();
        simFalse.Success.ShouldBeTrue();
        simFalse.ExecutedNodeIds.ShouldBe(["n_start", "n_cond", "n_std_tag"]);
        simFalse.ExecutedNodeIds.ShouldNotContain("n_ent_tag");
        simFalse.ExecutedNodeIds.ShouldNotContain("n_task");
        simFalse.FinalFacts["tier"]?.ToString().ShouldBe("Standard");
        simFalse.Steps.Count.ShouldBe(3);

        // 3. Verify executions list
        var execs = await s.Client.GetFromJsonAsync<List<VisualWorkflowExecutionDto>>($"/api/v1/workflows/{workflow.Id}/executions");
        execs.ShouldNotBeNull();
        execs.Count.ShouldBe(2);
        execs[0].Status.ShouldBe("Success");
        execs[0].WorkflowId.ShouldBe(workflow.Id);
        execs[0].Steps.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_visual_workflows()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA, "workflow-builder");
        await EnableFeatureAsync(tenantB, "workflow-builder");

        // Tenant A creates workflow
        var createResA = await tenantA.Client.PostAsJsonAsync("/api/v1/workflows", new SaveVisualWorkflowRequest(
            Name: "Confidential Tenant A Workflow",
            Description: null,
            EntityType: "deals",
            TriggerType: "record_created",
            IsActive: true,
            Nodes: [new("n1", "trigger", "Trigger A")],
            Edges: []
        ));
        createResA.StatusCode.ShouldBe(HttpStatusCode.Created);
        var workflowA = await createResA.Content.ReadFromJsonAsync<VisualWorkflowDto>();
        workflowA.ShouldNotBeNull();

        // Tenant B lists workflows -> empty
        var listB = await tenantB.Client.GetFromJsonAsync<List<VisualWorkflowDto>>("/api/v1/workflows?entityType=deals");
        listB.ShouldNotBeNull();
        listB.ShouldBeEmpty();

        // Tenant B tries to get Tenant A's workflow -> 404
        var getB = await tenantB.Client.GetAsync($"/api/v1/workflows/{workflowA.Id}");
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to simulate Tenant A's workflow -> 404
        var simB = await tenantB.Client.PostAsJsonAsync($"/api/v1/workflows/{workflowA.Id}/simulate", new SimulateWorkflowRequest(
            new Dictionary<string, object?>()
        ));
        simB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to list executions for Tenant A's workflow -> 404/empty
        var execsB = await tenantB.Client.GetFromJsonAsync<List<VisualWorkflowExecutionDto>>($"/api/v1/workflows/{workflowA.Id}/executions");
        execsB.ShouldNotBeNull();
        execsB.ShouldBeEmpty();

        // Tenant B tries to delete Tenant A's workflow -> 404
        var delB = await tenantB.Client.DeleteAsync($"/api/v1/workflows/{workflowA.Id}");
        delB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

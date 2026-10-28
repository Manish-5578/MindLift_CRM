using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Settings;

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// Tests definition creation, schema validation, dynamic JSON record payloads,
/// auto-incrementing record numbers, and strict multi-tenant isolation.
/// </summary>
public sealed class CustomObjectTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Feature_flag_off_blocks_custom_object_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        // Definitions list blocked (404)
        var listDefRes = await s.Client.GetAsync("/api/v1/custom-objects/definitions");
        listDefRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Definition create blocked (404)
        var createDefRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", new SaveCustomObjectDefinitionRequest(
            "Property", "Properties", "properties", null, null, null, true, null
        ));
        createDefRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Records list blocked (404)
        var listRecRes = await s.Client.GetAsync("/api/v1/custom-objects/properties/records");
        listRecRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Record create blocked (404)
        var createRecRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/properties/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>()
        ));
        createRecRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Custom_object_definition_crud_and_validation()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "custom-objects");

        var fields = new List<CustomObjectFieldDto>
        {
            new("address", "Property Address", null, "text", IsRequired: true),
            new("price", "Listing Price", null, "number", IsRequired: true),
            new("bedrooms", "Bedrooms", null, "number", IsRequired: false),
            new("property_type", "Property Type", null, "picklist", IsRequired: true, Options: ["Apartment", "Villa", "Townhouse"]),
            new("is_furnished", "Furnished", null, "boolean", IsRequired: false)
        };

        // 1. Create definition
        var createReq = new SaveCustomObjectDefinitionRequest(
            Name: "Property",
            PluralName: "Properties",
            Slug: "properties",
            Description: "Real estate listing object",
            Icon: "building",
            TitleField: "address",
            IsActive: true,
            Fields: fields
        );

        var createRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", createReq);
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<CustomObjectDefinitionDto>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("Property");
        created.Slug.ShouldBe("properties");
        created.TitleField.ShouldBe("address");
        created.Fields.Count.ShouldBe(5);

        // 2. Duplicate slug throws conflict (409)
        var dupRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", createReq);
        dupRes.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // 3. Invalid slug throws validation error (400)
        var invalidSlugRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", createReq with { Slug = "Invalid Slug!" });
        invalidSlugRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 4. Get by ID
        var getById = await s.Client.GetFromJsonAsync<CustomObjectDefinitionDto>($"/api/v1/custom-objects/definitions/{created.Id}");
        getById.ShouldNotBeNull();
        getById.Id.ShouldBe(created.Id);

        // 5. Get by slug
        var getBySlug = await s.Client.GetFromJsonAsync<CustomObjectDefinitionDto>("/api/v1/custom-objects/definitions/by-slug/properties");
        getBySlug.ShouldNotBeNull();
        getBySlug.Id.ShouldBe(created.Id);

        // 6. List definitions
        var list = await s.Client.GetFromJsonAsync<List<CustomObjectDefinitionDto>>("/api/v1/custom-objects/definitions");
        list.ShouldNotBeNull();
        list.Count.ShouldBe(1);
        list[0].Slug.ShouldBe("properties");

        // 7. Update definition
        var updateReq = createReq with
        {
            Description = "Updated description for properties",
            Icon = "home"
        };
        var updateRes = await s.Client.PutAsJsonAsync($"/api/v1/custom-objects/definitions/{created.Id}", updateReq);
        updateRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateRes.Content.ReadFromJsonAsync<CustomObjectDefinitionDto>();
        updated.ShouldNotBeNull();
        updated.Description.ShouldBe("Updated description for properties");
        updated.Icon.ShouldBe("home");
    }

    [Fact]
    public async Task Custom_object_record_crud_dynamic_validation_and_numbering()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "custom-objects");

        var fields = new List<CustomObjectFieldDto>
        {
            new("title", "Project Title", null, "text", IsRequired: true),
            new("budget", "Budget Amount", null, "number", IsRequired: true),
            new("status", "Project Status", null, "picklist", IsRequired: true, Options: ["Planning", "Active", "Completed"]),
            new("is_billable", "Billable", null, "boolean", IsRequired: false)
        };

        var defRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", new SaveCustomObjectDefinitionRequest(
            Name: "Project",
            PluralName: "Projects",
            Slug: "projects",
            Description: "Client delivery projects",
            Icon: "briefcase",
            TitleField: "title",
            IsActive: true,
            Fields: fields
        ));
        defRes.StatusCode.ShouldBe(HttpStatusCode.Created);

        // 1. Missing required field "title" -> 400
        var missingTitleRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/projects/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>
            {
                ["budget"] = 50000,
                ["status"] = "Planning"
            }
        ));
        missingTitleRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 2. Invalid picklist value -> 400
        var invalidStatusRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/projects/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>
            {
                ["title"] = "CRM Migration",
                ["budget"] = 50000,
                ["status"] = "UnknownStatus"
            }
        ));
        invalidStatusRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 3. Invalid numeric value -> 400
        var invalidNumRes = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/projects/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>
            {
                ["title"] = "CRM Migration",
                ["budget"] = "non-numeric-value",
                ["status"] = "Planning"
            }
        ));
        invalidNumRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 4. Create valid Record #1
        var rec1Res = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/projects/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>
            {
                ["title"] = "CRM Implementation Phase 1",
                ["budget"] = 75000,
                ["status"] = "Active",
                ["is_billable"] = true
            }
        ));
        rec1Res.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rec1 = await rec1Res.Content.ReadFromJsonAsync<CustomObjectRecordDto>();
        rec1.ShouldNotBeNull();
        rec1.RecordNumber.ShouldBe(1);
        rec1.DisplayName.ShouldBe("CRM Implementation Phase 1");
        rec1.DefinitionSlug.ShouldBe("projects");

        // 5. Create valid Record #2
        var rec2Res = await s.Client.PostAsJsonAsync("/api/v1/custom-objects/projects/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>
            {
                ["title"] = "Cloud Migration 2026",
                ["budget"] = 120000,
                ["status"] = "Planning",
                ["is_billable"] = false
            }
        ));
        rec2Res.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rec2 = await rec2Res.Content.ReadFromJsonAsync<CustomObjectRecordDto>();
        rec2.ShouldNotBeNull();
        rec2.RecordNumber.ShouldBe(2);
        rec2.DisplayName.ShouldBe("Cloud Migration 2026");

        // 6. Get record by ID
        var getRec = await s.Client.GetFromJsonAsync<CustomObjectRecordDto>($"/api/v1/custom-objects/projects/records/{rec1.Id}");
        getRec.ShouldNotBeNull();
        getRec.Id.ShouldBe(rec1.Id);
        getRec.DisplayName.ShouldBe("CRM Implementation Phase 1");

        // 7. List records with pagination
        var paged = await s.Client.GetFromJsonAsync<CustomObjectRecordPagedResult>("/api/v1/custom-objects/projects/records?page=1&pageSize=10");
        paged.ShouldNotBeNull();
        paged.TotalCount.ShouldBe(2);
        paged.Items.Count.ShouldBe(2);

        // 8. Search records by title
        var searchRes = await s.Client.GetFromJsonAsync<CustomObjectRecordPagedResult>("/api/v1/custom-objects/projects/records?search=Cloud");
        searchRes.ShouldNotBeNull();
        searchRes.TotalCount.ShouldBe(1);
        searchRes.Items[0].Id.ShouldBe(rec2.Id);

        // 9. Update record
        var updateRecRes = await s.Client.PutAsJsonAsync($"/api/v1/custom-objects/projects/records/{rec1.Id}", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?>
            {
                ["title"] = "CRM Implementation - Delivered",
                ["budget"] = 80000,
                ["status"] = "Completed",
                ["is_billable"] = true
            }
        ));
        updateRecRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedRec = await updateRecRes.Content.ReadFromJsonAsync<CustomObjectRecordDto>();
        updatedRec.ShouldNotBeNull();
        updatedRec.DisplayName.ShouldBe("CRM Implementation - Delivered");
        updatedRec.RecordNumber.ShouldBe(1);

        // 10. Delete record
        var delRes = await s.Client.DeleteAsync($"/api/v1/custom-objects/projects/records/{rec1.Id}");
        delRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfterDel = await s.Client.GetAsync($"/api/v1/custom-objects/projects/records/{rec1.Id}");
        getAfterDel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_custom_objects()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA, "custom-objects");
        await EnableFeatureAsync(tenantB, "custom-objects");

        // Tenant A creates custom object "Vehicle"
        var createDefA = await tenantA.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", new SaveCustomObjectDefinitionRequest(
            Name: "Vehicle",
            PluralName: "Vehicles",
            Slug: "vehicles",
            Description: "Fleet vehicles",
            Icon: "car",
            TitleField: "vin",
            IsActive: true,
            Fields: [new("vin", "Vehicle VIN", null, "text", IsRequired: true)]
        ));
        createDefA.StatusCode.ShouldBe(HttpStatusCode.Created);
        var defA = await createDefA.Content.ReadFromJsonAsync<CustomObjectDefinitionDto>();
        defA.ShouldNotBeNull();

        // Tenant A creates a record
        var recA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/custom-objects/vehicles/records", new SaveCustomObjectRecordRequest(
            new Dictionary<string, object?> { ["vin"] = "VIN-123456789" }
        ))).Content.ReadFromJsonAsync<CustomObjectRecordDto>();
        recA.ShouldNotBeNull();

        // Tenant B lists definitions -> empty
        var defsB = await tenantB.Client.GetFromJsonAsync<List<CustomObjectDefinitionDto>>("/api/v1/custom-objects/definitions");
        defsB.ShouldNotBeNull();
        defsB.ShouldBeEmpty();

        // Tenant B tries to get Tenant A's definition -> 404
        var getDefB = await tenantB.Client.GetAsync($"/api/v1/custom-objects/definitions/{defA.Id}");
        getDefB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to list records for "vehicles" -> 404 (definition doesn't exist for Tenant B)
        var listRecB = await tenantB.Client.GetAsync("/api/v1/custom-objects/vehicles/records");
        listRecB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to get Tenant A's record -> 404
        var getRecB = await tenantB.Client.GetAsync($"/api/v1/custom-objects/vehicles/records/{recA.Id}");
        getRecB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B can create its own "Vehicle" with slug "vehicles" independently without conflict
        var createDefB = await tenantB.Client.PostAsJsonAsync("/api/v1/custom-objects/definitions", new SaveCustomObjectDefinitionRequest(
            Name: "Company Vehicle",
            PluralName: "Company Vehicles",
            Slug: "vehicles",
            Description: "Tenant B vehicles",
            Icon: "truck",
            TitleField: "plate",
            IsActive: true,
            Fields: [new("plate", "Plate Number", null, "text", IsRequired: true)]
        ));
        createDefB.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}

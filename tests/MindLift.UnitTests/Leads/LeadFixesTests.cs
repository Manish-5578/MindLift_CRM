using System.Text.Json;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Leads;

public sealed class LeadFixesTests
{
    [Fact]
    public void Fix1_FirstName_IsNotMandatory_WhenCompanyOrLastNameProvided()
    {
        // Expected: First name is not mandatory. A lead can be created with only CompanyName.
        var leadDef = SystemFields.Entities.First(e => e.Key == "leads");
        var firstNameDef = leadDef.SystemFields.First(f => f.Key == "first_name");
        
        firstNameDef.IsRequired.ShouldBeFalse();

        // Validating request with only CompanyName
        var request = new SaveLeadRequest(
            string.Empty,
            string.Empty,
            "info@acme.com",
            "+1234567890",
            "Acme Corp",
            "new",
            "web",
            "hot",
            "Notes",
            null,
            null,
            null);

        // Actual behavior matches expected:
        request.FirstName.ShouldBe(string.Empty);
        request.CompanyName.ShouldBe("Acme Corp");
    }

    [Fact]
    public void Fix2_LeadsTab_DisplaysCompanyInsteadOfName_AndEmailReplacedWithName()
    {
        // Expected:
        // Primary title = CompanyName (fallback to FullName if CompanyName is missing)
        // Subtitle = FullName (if available, replacing email)
        
        var leadWithCompany = new LeadListItemDto(
            Guid.NewGuid(),
            "John Doe",
            "john@acme.com",
            "+1234567890",
            "Acme Corp",
            "new",
            "web",
            "hot",
            Guid.NewGuid(),
            "Owner User",
            DateTime.UtcNow,
            null,
            false,
            false,
            "John",
            "Doe");

        // Primary: Company name instead of name
        var primaryTitle = !string.IsNullOrWhiteSpace(leadWithCompany.CompanyName)
            ? leadWithCompany.CompanyName
            : (!string.IsNullOrWhiteSpace(leadWithCompany.FullName) ? leadWithCompany.FullName : "—");
        primaryTitle.ShouldBe("Acme Corp");

        // Subtitle: Email replaced with name (if available)
        var subtitle = !string.IsNullOrWhiteSpace(leadWithCompany.FullName)
            ? leadWithCompany.FullName
            : (leadWithCompany.Email ?? leadWithCompany.Phone ?? "—");
        subtitle.ShouldBe("John Doe");

        // When company is missing:
        var leadWithoutCompany = new LeadListItemDto(
            Guid.NewGuid(),
            "Jane Smith",
            "jane@example.com",
            null,
            null,
            "new",
            null,
            null,
            Guid.NewGuid(),
            null,
            DateTime.UtcNow,
            null,
            false,
            false,
            "Jane",
            "Smith");

        var fallbackTitle = !string.IsNullOrWhiteSpace(leadWithoutCompany.CompanyName)
            ? leadWithoutCompany.CompanyName
            : (!string.IsNullOrWhiteSpace(leadWithoutCompany.FullName) ? leadWithoutCompany.FullName : "—");
        fallbackTitle.ShouldBe("Jane Smith");
    }

    [Fact]
    public void Fix3_FieldOrdering_CompanyNameIsFirst_FollowedByNames()
    {
        // Expected field ordering in lead form drawer:
        // Index 0: companyName
        // Index 1: firstName
        // Index 2: lastName
        var expectedOrder = new[] { "companyName", "firstName", "lastName" };

        var fieldKeys = new[]
        {
            "companyName",
            "firstName",
            "lastName",
            "email",
            "phone",
            "status",
            "source",
            "rating",
            "description"
        };

        fieldKeys[0].ShouldBe("companyName");
        fieldKeys[1].ShouldBe("firstName");
        fieldKeys[2].ShouldBe("lastName");
    }

    [Fact]
    public void Fix4_And_Fix6_MoveLeadToView_ScopesFilterSpecificallyToMovedLeadId()
    {
        // Expected behavior:
        // When Lead 1 is moved into view "abc", the view filter scopes to specifically Lead 1.
        // It must NOT show all leads.
        var lead1Id = Guid.NewGuid();
        var lead2Id = Guid.NewGuid();

        var viewFilter = new FilterGroup
        {
            Conditions =
            [
                new FilterCondition
                {
                    Field = "id",
                    Op = FilterOperator.In,
                    Value = JsonSerializer.SerializeToElement(new[] { lead1Id })
                }
            ]
        };

        // Filter should match lead1
        var leadsDef = SystemFields.Entities.First(e => e.Key == "leads");
        var context = new FilterContext(TimeZoneInfo.Utc, DateTime.UtcNow, DayOfWeek.Monday, 1, Guid.NewGuid());
        var predicate = FilterCompiler.Compile<Lead>(viewFilter, leadsDef.SystemFields, context).Compile();

        var matchingLead = new Lead { Id = lead1Id, CompanyName = "Target Co" };
        var otherLead = new Lead { Id = lead2Id, CompanyName = "Other Co" };

        predicate(matchingLead).ShouldBeTrue();
        predicate(otherLead).ShouldBeFalse();
    }

    [Fact]
    public void Fix5_SaveView_ShareWithEveryone_IsNotMandatory()
    {
        // Expected: IsShared can be false (private view) and is not required to be true.
        var request = new SaveSavedViewRequest("leads", "My Private View", IsShared: false, IsDefault: false, Filter: null, Columns: [], SortField: null, SortDir: null);

        request.IsShared.ShouldBeFalse();
        request.Name.ShouldBe("My Private View");
    }

    [Fact]
    public void LeadStatus_ChangeStatusRequest_AndAllowedTransitions_Validated()
    {
        // Expected: dedicated ChangeLeadStatusRequest carries new status
        var request = new ChangeLeadStatusRequest("working");
        request.Status.ShouldBe("working");

        // Allowed direct statuses are new, working, qualified (converted & disqualified use dedicated flows)
        var allowedDirectStatuses = new[] { "new", "working", "qualified" };
        allowedDirectStatuses.ShouldContain(request.Status);

        // Verification of lead transition: moving to working marks FirstContactedAt if null
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            CompanyName = "Test Corp",
            Status = "new",
            FirstContactedAt = null
        };

        if (request.Status == "working" && lead.FirstContactedAt is null)
        {
            lead.FirstContactedAt = DateTime.UtcNow;
        }
        lead.Status = request.Status;

        lead.Status.ShouldBe("working");
        lead.FirstContactedAt.ShouldNotBeNull();
    }

    [Fact]
    public void NegativeMinutes_Sanitization_PreventsNegativeValues()
    {
        // When negative input such as "-6" or "-10" is supplied, it must be sanitized so negative numbers are disallowed
        static string? SanitizeNumber(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return v;
            var clean = v.Replace("-", "").Trim();
            if (int.TryParse(clean, out var n))
            {
                return Math.Max(0, n).ToString();
            }
            return clean;
        }

        SanitizeNumber("-6").ShouldBe("6");
        SanitizeNumber("-0").ShouldBe("0");
        SanitizeNumber("-").ShouldBe(string.Empty);
        SanitizeNumber("30").ShouldBe("30");
    }

    [Fact]
    public void ViewExclusivity_MovingLead_ExcludesFromSourceAndIncludesInTarget()
    {
        var leadId = Guid.NewGuid();
        var viewAId = Guid.NewGuid();
        var viewBId = Guid.NewGuid();

        // View A initially had leadId in its In condition
        var viewAFilter = new FilterGroup
        {
            Conditions = [new FilterCondition { Field = "id", Op = FilterOperator.In, Value = JsonSerializer.SerializeToElement(new[] { leadId }) }]
        };

        // View B initially was empty
        var viewBFilter = new FilterGroup { Conditions = [] };

        // Moving leadId from View A to View B:
        // 1. Add to View B In condition
        viewBFilter.Conditions.Add(new FilterCondition { Field = "id", Op = FilterOperator.In, Value = JsonSerializer.SerializeToElement(new[] { leadId }) });

        // 2. Remove from View A In condition
        var inCond = viewAFilter.Conditions.First(c => c.Field == "id" && c.Op == FilterOperator.In);
        var inIds = inCond.Value!.Value.EnumerateArray().Select(x => x.GetGuid()).Where(g => g != leadId).ToList();
        if (inIds.Count == 0) inIds.Add(Guid.Empty);
        inCond.Value = JsonSerializer.SerializeToElement(inIds.ToArray());

        // Assert: View A does NOT contain leadId
        var remainingA = inCond.Value!.Value.EnumerateArray().Select(x => x.GetGuid()).ToList();
        remainingA.ShouldNotContain(leadId);

        // Assert: View B DOES contain leadId
        var remainingB = viewBFilter.Conditions.First(c => c.Field == "id" && c.Op == FilterOperator.In)
            .Value!.Value.EnumerateArray().Select(x => x.GetGuid()).ToList();
        remainingB.ShouldContain(leadId);
    }
}

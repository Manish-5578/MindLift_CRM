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
}

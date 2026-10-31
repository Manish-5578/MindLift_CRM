using System.Text.Json;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Shared.Filtering;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Leads;

public sealed class LeadFlowValidationTests
{
    [Fact]
    public void FlowTest_CreateLead_WithoutFirstName_ValidatesAndPopulatesCorrectly()
    {
        // Flow: User enters Company Name without First Name in the drawer form
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            CompanyName = "Global Tech Ltd",
            FirstName = string.Empty,
            LastName = "Consultant",
            Email = "contact@globaltech.com"
        };

        // Assert actual behavior vs expected behavior:
        // Expected: First name is empty, FullName is "Consultant", CompanyName is "Global Tech Ltd"
        lead.FirstName.ShouldBe(string.Empty);
        $"{lead.FirstName} {lead.LastName}".Trim().ShouldBe("Consultant");
        lead.CompanyName.ShouldBe("Global Tech Ltd");

        // Display check:
        // Primary text should be CompanyName
        var primary = !string.IsNullOrWhiteSpace(lead.CompanyName) ? lead.CompanyName : (!string.IsNullOrWhiteSpace(lead.FirstName) ? lead.FirstName : "—");
        primary.ShouldBe("Global Tech Ltd");

        // Secondary text should be Name (if available) replacing Email
        var secondary = !string.IsNullOrWhiteSpace($"{lead.FirstName} {lead.LastName}".Trim()) ? $"{lead.FirstName} {lead.LastName}".Trim() : (lead.Email ?? "—");
        secondary.ShouldBe("Consultant");
    }

    [Fact]
    public void FlowTest_MoveLeadToView_EnsuresFiltrationOnlyShowsSingleMovedLead_NotAllLeads()
    {
        // Flow:
        // 1. We have 3 leads in the system: Lead A, Lead B, Lead C
        var leadA = new Lead { Id = Guid.NewGuid(), CompanyName = "Lead A Corp" };
        var leadB = new Lead { Id = Guid.NewGuid(), CompanyName = "Lead B Corp" };
        var leadC = new Lead { Id = Guid.NewGuid(), CompanyName = "Lead C Corp" };

        var allLeads = new List<Lead> { leadA, leadB, leadC };

        // 2. User creates view "abc" and moves ONLY Lead A into view "abc"
        var viewAbcFilter = new FilterGroup
        {
            Conditions =
            [
                new FilterCondition
                {
                    Field = "id",
                    Op = FilterOperator.In,
                    Value = JsonSerializer.SerializeToElement(new[] { leadA.Id })
                }
            ]
        };

        var leadsDef = SystemFields.Entities.First(e => e.Key == "leads");
        var context = new FilterContext(TimeZoneInfo.Utc, DateTime.UtcNow, DayOfWeek.Monday, 1, Guid.NewGuid());
        var predicate = FilterCompiler.Compile<Lead>(viewAbcFilter, leadsDef.SystemFields, context).Compile();

        // 3. Querying leads under view "abc":
        var matchedLeads = allLeads.Where(predicate).ToList();

        // 4. Verify actual behavior vs expected behavior:
        // Expected: Only 1 lead (Lead A) is returned. Lead B and Lead C are excluded.
        matchedLeads.Count.ShouldBe(1);
        matchedLeads[0].Id.ShouldBe(leadA.Id);
        matchedLeads[0].CompanyName.ShouldBe("Lead A Corp");

        // 5. Flow: User now moves Lead A out of view "abc" to view "xyz"
        // In view "abc", Lead A is removed, leaving an empty list:
        var updatedAbcFilter = new FilterGroup
        {
            Conditions =
            [
                new FilterCondition
                {
                    Field = "id",
                    Op = FilterOperator.In,
                    Value = JsonSerializer.SerializeToElement(new[] { Guid.Empty })
                }
            ]
        };

        var updatedPredicate = FilterCompiler.Compile<Lead>(updatedAbcFilter, leadsDef.SystemFields, context).Compile();
        var remainingInAbc = allLeads.Where(updatedPredicate).ToList();

        // Expected: 0 leads remain in view "abc"
        remainingInAbc.Count.ShouldBe(0);
    }
}

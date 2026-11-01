using System.Text;
using ClosedXML.Excel;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using MindLift.Shared.Imports;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Leads;

public sealed class LeadExcelAndImportTests
{
    [Fact]
    public void GenerateLeadTemplateExcel_ContainsLeadsAndHelpSheets_WithPreFilledHeadings_AndNoPrefilledDataRows()
    {
        // Act: Generate actual template Excel workbook via LeadService
        var bytes = LeadService.GenerateTemplateExcel();
        using var ms = new MemoryStream(bytes);

        // Verify loaded workbook
        using var readWb = new XLWorkbook(ms);
        readWb.Worksheets.Count.ShouldBe(2);

        var leadsWorksheet = readWb.Worksheet("Leads");
        leadsWorksheet.ShouldNotBeNull();
        leadsWorksheet.Cell(1, 1).GetString().ShouldBe("Company");
        leadsWorksheet.Cell(1, 2).GetString().ShouldBe("First Name");
        leadsWorksheet.Cell(1, 3).GetString().ShouldBe("Last Name");
        leadsWorksheet.Cell(1, 4).GetString().ShouldBe("Address");
        leadsWorksheet.Cell(1, 5).GetString().ShouldBe("Email");
        leadsWorksheet.Cell(1, 6).GetString().ShouldBe("Phone");
        leadsWorksheet.Cell(1, 7).GetString().ShouldBe("Status");
        leadsWorksheet.Cell(1, 8).GetString().ShouldBe("Source");
        leadsWorksheet.Cell(1, 9).GetString().ShouldBe("Rating");
        leadsWorksheet.Cell(1, 10).GetString().ShouldBe("Notes");

        // Requirement 4: No prefilled leads inside the template (only the header row)
        leadsWorksheet.LastRowUsed()!.RowNumber().ShouldBe(1);
        leadsWorksheet.Cell(2, 1).GetString().ShouldBe(string.Empty);

        var helpWorksheet = readWb.Worksheet("Help");
        helpWorksheet.ShouldNotBeNull();
        helpWorksheet.Cell(1, 1).GetString().ShouldBe("Field Name");
        helpWorksheet.Cell(1, 2).GetString().ShouldBe("Requirement");
        helpWorksheet.Cell(1, 3).GetString().ShouldBe("Description");
        helpWorksheet.Cell(1, 4).GetString().ShouldBe("Allowed / Example Values");

        // Requirement 2: Address must be marked Mandatory in template
        var foundAddressRow = false;
        var lastHelpRow = helpWorksheet.LastRowUsed()!.RowNumber();
        for (var r = 2; r <= lastHelpRow; r++)
        {
            if (helpWorksheet.Cell(r, 1).GetString() == "Address")
            {
                helpWorksheet.Cell(r, 2).GetString().ShouldBe("Mandatory");
                foundAddressRow = true;
                break;
            }
        }
        foundAddressRow.ShouldBeTrue("Address row should exist in Help sheet and be marked Mandatory.");
    }

    [Fact]
    public void ParseRowsFromStream_ParsesExcelStream_Correctly()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Leads");
        sheet.Cell(1, 1).Value = "Company";
        sheet.Cell(1, 2).Value = "First Name";
        sheet.Cell(1, 3).Value = "Address";
        sheet.Cell(1, 4).Value = "Email";

        sheet.Cell(2, 1).Value = "Nova Corp";
        sheet.Cell(2, 2).Value = "Alice";
        sheet.Cell(2, 3).Value = "123 Main St";
        sheet.Cell(2, 4).Value = "alice@nova.com";

        sheet.Cell(3, 1).Value = "Starlight Ltd";
        sheet.Cell(3, 2).Value = "Bob";
        sheet.Cell(3, 3).Value = "456 Elm Ave";
        sheet.Cell(3, 4).Value = "bob@starlight.com";

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var rows = LeadService.ParseRowsFromStream(ms, "leads.xlsx");

        rows.Count.ShouldBe(3);
        rows[0].ShouldBe(["Company", "First Name", "Address", "Email"]);
        rows[1].ShouldBe(["Nova Corp", "Alice", "123 Main St", "alice@nova.com"]);
        rows[2].ShouldBe(["Starlight Ltd", "Bob", "456 Elm Ave", "bob@starlight.com"]);
    }

    [Fact]
    public void ParseRowsFromStream_ParsesCsvStream_Correctly()
    {
        var csv = "Company,First Name,Address,Email\nNova Corp,Alice,123 Main St,alice@nova.com\nStarlight Ltd,Bob,456 Elm Ave,bob@starlight.com\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var rows = LeadService.ParseRowsFromStream(ms, "leads.csv");

        rows.Count.ShouldBe(3);
        rows[0].ShouldBe(["Company", "First Name", "Address", "Email"]);
        rows[1].ShouldBe(["Nova Corp", "Alice", "123 Main St", "alice@nova.com"]);
        rows[2].ShouldBe(["Starlight Ltd", "Bob", "456 Elm Ave", "bob@starlight.com"]);
    }

    [Fact]
    public void LeadImportColumns_MapsExcelHeadings_WithAddressStatusAndRating()
    {
        var headers = new[] { "Company", "First Name", "Last Name", "Address", "Email", "Phone", "Status", "Source", "Rating", "Notes" };
        var map = LeadImportColumns.Map(headers);

        map["company_name"].ShouldBe(0);
        map["first_name"].ShouldBe(1);
        map["last_name"].ShouldBe(2);
        map["address"].ShouldBe(3);
        map["email"].ShouldBe(4);
        map["phone"].ShouldBe(5);
        map["status"].ShouldBe(6);
        map["source"].ShouldBe(7);
        map["rating"].ShouldBe(8);
        map["description"].ShouldBe(9);
    }

    [Fact]
    public void ImportHistorySummaryDto_AggregatesMetrics_Accurately()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var item1 = new ImportHistoryItemDto(
            id1, "leads", "q1_leads.xlsx", 100, 95, 3, 2, "completed_with_errors",
            ["Row 12: invalid email"], Guid.NewGuid(), "Admin User", DateTime.UtcNow.AddDays(-1));

        var item2 = new ImportHistoryItemDto(
            id2, "leads", "q2_leads.csv", 50, 50, 0, 0, "completed",
            [], Guid.NewGuid(), "Admin User", DateTime.UtcNow);

        var items = new List<ImportHistoryItemDto> { item1, item2 };
        var summary = new ImportHistorySummaryDto(
            items.Count,
            items.Sum(i => i.TotalRows),
            items.Sum(i => i.ImportedCount),
            items.Sum(i => i.SkippedCount),
            items.Sum(i => i.FailedCount),
            items);

        summary.TotalImports.ShouldBe(2);
        summary.TotalRows.ShouldBe(150);
        summary.TotalImported.ShouldBe(145);
        summary.TotalSkipped.ShouldBe(3);
        summary.TotalFailed.ShouldBe(2);
        summary.Items.Count.ShouldBe(2);
        summary.Items[0].FileName.ShouldBe("q1_leads.xlsx");
        summary.Items[1].FileName.ShouldBe("q2_leads.csv");
    }

    [Fact]
    public void ImportHistory_Entity_StoresExpectedProperties()
    {
        var history = new ImportHistory
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EntityType = "leads",
            FileName = "bulk_upload_november.xlsx",
            TotalRows = 500,
            ImportedCount = 480,
            SkippedCount = 15,
            FailedCount = 5,
            Status = "completed_with_errors",
            Errors = ["Row 42: email format invalid", "Row 88: duplicate contact"],
            ImportedById = Guid.NewGuid(),
            ImportedByName = "Sarah Connor",
            CreatedAt = DateTime.UtcNow
        };

        history.EntityType.ShouldBe("leads");
        history.FileName.ShouldBe("bulk_upload_november.xlsx");
        history.TotalRows.ShouldBe(500);
        history.ImportedCount.ShouldBe(480);
        history.SkippedCount.ShouldBe(15);
        history.FailedCount.ShouldBe(5);
        history.Status.ShouldBe("completed_with_errors");
        history.Errors.Length.ShouldBe(2);
        history.ImportedByName.ShouldBe("Sarah Connor");
    }
}

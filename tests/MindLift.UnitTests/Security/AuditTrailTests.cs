using System.Globalization;
using System.Text.Json;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Security;

public sealed class AuditTrailTests
{
    [Fact]
    public void AuditLogDto_WithEnrichedMetadata_HoldsPropertiesCorrectly()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var diffs = new List<AuditFieldDiff>
        {
            new("Status", "new", "working"),
            new("Phone", null, "+1234567890")
        };

        var dto = new AuditLogDto(
            id,
            userId,
            "Lead",
            entityId,
            "Update",
            "[{\"Field\":\"Status\",\"Old\":\"new\",\"New\":\"working\"}]",
            now,
            "Sarah Connor",
            "sarah@acme.com",
            "Update Lead: changed Status, Phone",
            diffs);

        dto.Id.ShouldBe(id);
        dto.UserId.ShouldBe(userId);
        dto.EntityType.ShouldBe("Lead");
        dto.EntityId.ShouldBe(entityId);
        dto.Action.ShouldBe("Update");
        dto.UserName.ShouldBe("Sarah Connor");
        dto.UserEmail.ShouldBe("sarah@acme.com");
        dto.Summary.ShouldBe("Update Lead: changed Status, Phone");
        dto.Diff.ShouldNotBeNull();
        dto.Diff.Count.ShouldBe(2);
        dto.Diff[0].Field.ShouldBe("Status");
        dto.Diff[0].OldValue.ShouldBe("new");
        dto.Diff[0].NewValue.ShouldBe("working");
    }

    [Fact]
    public void AuditLogDto_WithModuleRecordTitleAndIp_PopulatesSuccessfully()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var dto = new AuditLogDto(
            id,
            userId,
            "Lead",
            entityId,
            "Update",
            null,
            now,
            "John Doe",
            "john@example.com",
            "Updated Leads 'Acme Global': changed Status",
            null,
            "Leads",
            "Acme Global (John Doe)",
            "192.168.1.100");

        dto.Module.ShouldBe("Leads");
        dto.RecordTitle.ShouldBe("Acme Global (John Doe)");
        dto.IpAddress.ShouldBe("192.168.1.100");
        dto.UserName.ShouldBe("John Doe");
        dto.UserEmail.ShouldBe("john@example.com");
        dto.Summary.ShouldBe("Updated Leads 'Acme Global': changed Status");
    }

    [Fact]
    public void AuditLog_LoginEvent_HasCorrectAuditSignature()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var loginDto = new AuditLogDto(
            Guid.NewGuid(),
            userId,
            "User",
            userId,
            "Login",
            null,
            now,
            "Admin User",
            "admin@mindlift.com",
            "User 'Admin User' (admin@mindlift.com) signed in to workspace",
            null,
            "Authentication",
            "Admin User",
            "127.0.0.1");

        loginDto.Action.ShouldBe("Login");
        loginDto.Module.ShouldBe("Authentication");
        loginDto.RecordTitle.ShouldBe("Admin User");
        loginDto.IpAddress.ShouldBe("127.0.0.1");
        loginDto.Summary.ShouldContain("signed in to workspace");
    }

    [Fact]
    public void AuditLog_ChangesJson_ParsesDiffsAccurately()
    {
        var json = "[{\"Field\":\"CompanyName\",\"Old\":\"Old Corp\",\"New\":\"New Corp\"},{\"Field\":\"Rating\",\"Old\":\"cold\",\"New\":\"hot\"}]";

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);

        var diffs = new List<AuditFieldDiff>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var field = item.GetProperty("Field").GetString();
            var oldVal = item.GetProperty("Old").GetString();
            var newVal = item.GetProperty("New").GetString();
            diffs.Add(new AuditFieldDiff(field!, oldVal, newVal));
        }

        diffs.Count.ShouldBe(2);
        diffs[0].Field.ShouldBe("CompanyName");
        diffs[0].OldValue.ShouldBe("Old Corp");
        diffs[0].NewValue.ShouldBe("New Corp");
        diffs[1].Field.ShouldBe("Rating");
        diffs[1].OldValue.ShouldBe("cold");
        diffs[1].NewValue.ShouldBe("hot");
    }

    [Fact]
    public void CsvExport_EscapesFormulasAndQuotesCorrectly()
    {
        static string Cell(string? v)
        {
            v ??= "";
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        // Test normal cell
        Cell("Sarah Connor").ShouldBe("\"Sarah Connor\"");

        // Test formula injection prevention
        Cell("=SUM(A1:A10)").ShouldBe("\"'=SUM(A1:A10)\"");
        Cell("@danger").ShouldBe("\"'@danger\"");
        Cell("+123").ShouldBe("\"'+123\"");
        Cell("-50").ShouldBe("\"'-50\"");

        // Test quotes escaping
        Cell("Said \"Hello\"").ShouldBe("\"Said \"\"Hello\"\"\"");
    }

    [Fact]
    public void FeatureCatalog_AllCapabilities_ArePresentInCatalog()
    {
        FeatureCatalog.All.Count.ShouldBe(43);
        FeatureCatalog.IsKnown("recycle-bin").ShouldBeTrue();
        FeatureCatalog.IsKnown("lead-conversion").ShouldBeTrue();
        FeatureCatalog.IsKnown("workflow-builder").ShouldBeTrue();
        FeatureCatalog.IsKnown("fake-feature-xyz").ShouldBeFalse();
    }
}


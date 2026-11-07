using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Leads;

public sealed class LeadNotesAndActivityEnhancementTests
{
    [Fact]
    public void LeadNoteDto_Holds_Author_Content_And_Timestamp()
    {
        var noteId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var note = new LeadNoteDto(noteId, leadId, "Met with client. Interested in enterprise tier.", authorId, "Sara Connor", now);

        note.Id.ShouldBe(noteId);
        note.LeadId.ShouldBe(leadId);
        note.AuthorId.ShouldBe(authorId);
        note.AuthorName.ShouldBe("Sara Connor");
        note.Content.ShouldBe("Met with client. Interested in enterprise tier.");
        note.CreatedAtUtc.ShouldBe(now);
    }

    [Fact]
    public void ImportPreviewDto_Provides_Accurate_Validation_Counts()
    {
        var preview = new ImportPreviewDto(
            FileName: "leads_25.xlsx",
            Headers: ["Company", "First Name", "Last Name", "Address", "Email", "Phone"],
            Rows: [["Acme Corp", "John", "Doe", "123 Main St", "john@acme.com", "+123456789"]],
            TotalRows: 25,
            Matches: [],
            HasRequired: true,
            Error: null,
            ValidRows: 23,
            InvalidRows: 1,
            DuplicateRows: 1
        );

        preview.TotalRows.ShouldBe(25);
        preview.ValidRows.ShouldBe(23);
        preview.InvalidRows.ShouldBe(1);
        preview.DuplicateRows.ShouldBe(1);
        preview.HasRequired.ShouldBeTrue();
    }

    [Fact]
    public void RescheduleActivityRequest_Carries_NextDueAt_And_Reason()
    {
        var nextDue = DateTime.UtcNow.AddDays(2);
        var req = new RescheduleActivityRequest(
            NextDueAt: nextDue,
            RescheduleReason: "Customer requested callback",
            Remarks: "Requested to call after budget meeting",
            DurationMinutes: 30
        );

        req.NextDueAt.ShouldBe(nextDue);
        req.RescheduleReason.ShouldBe("Customer requested callback");
        req.Remarks.ShouldBe("Requested to call after budget meeting");
        req.DurationMinutes.ShouldBe(30);
    }

    [Fact]
    public void RecordOutcomeRequest_Carries_Outcome_Duration_And_FollowUp()
    {
        var followUpDue = DateTime.UtcNow.AddDays(3);
        var req = new RecordOutcomeRequest(
            Outcome: "Connected",
            Notes: "Discussed demo requirements",
            DurationMinutes: 25,
            ScheduleFollowUp: true,
            FollowUpDueAt: followUpDue,
            FollowUpSubject: "Follow-up Call: Contract Review"
        );

        req.Outcome.ShouldBe("Connected");
        req.Notes.ShouldBe("Discussed demo requirements");
        req.DurationMinutes.ShouldBe(25);
        req.ScheduleFollowUp.ShouldBeTrue();
        req.FollowUpDueAt.ShouldBe(followUpDue);
        req.FollowUpSubject.ShouldBe("Follow-up Call: Contract Review");
    }

    [Fact]
    public void LeadNote_Entity_Maps_Required_Audit_Properties()
    {
        var leadId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var note = new LeadNote
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            LeadId = leadId,
            Content = "Important account briefing",
            AuthorId = authorId,
            AuthorName = "Alex Mercer",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        note.LeadId.ShouldBe(leadId);
        note.AuthorId.ShouldBe(authorId);
        note.AuthorName.ShouldBe("Alex Mercer");
        note.Content.ShouldBe("Important account briefing");
    }
}


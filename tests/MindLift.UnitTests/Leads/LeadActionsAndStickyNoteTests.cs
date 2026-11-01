using System;
using System.Collections.Generic;
using MindLift.Shared.Contracts;
using MindLift.UI.Components;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Leads;

public sealed class LeadActionsAndStickyNoteTests
{
    [Fact]
    public void LeadListItemDto_Includes_Description_For_StickyNotes()
    {
        var id = Guid.NewGuid();
        var dto = new LeadListItemDto(
            id,
            "Alice Smith",
            "alice@example.com",
            "+123456789",
            "Acme Corp",
            "new",
            "website",
            "hot",
            Guid.NewGuid(),
            "Owner User",
            DateTime.UtcNow,
            null,
            false,
            false,
            "Alice",
            "Smith",
            "123 Tech Lane",
            "High priority lead from tech expo. Prefers morning contact.");

        dto.Description.ShouldBe("High priority lead from tech expo. Prefers morning contact.");
        dto.Address.ShouldBe("123 Tech Lane");
    }

    [Fact]
    public void ConvertLeadRequest_Carries_Contact_Names_For_Partial_Leads()
    {
        var request = new ConvertLeadRequest(
            AccountMode: "create",
            ExistingAccountId: null,
            AccountName: "Initech",
            ContactMode: "create",
            ExistingContactId: null,
            CreateDeal: true,
            DealName: "Initech Initial Deal",
            PipelineId: Guid.NewGuid(),
            StageId: Guid.NewGuid(),
            Amount: 50000m,
            Currency: "USD",
            ExpectedCloseDate: DateTime.UtcNow.AddDays(30),
            ContactFirstName: "Peter",
            ContactLastName: "Gibbons");

        request.ContactFirstName.ShouldBe("Peter");
        request.ContactLastName.ShouldBe("Gibbons");
        request.CreateDeal.ShouldBeTrue();
    }

    [Fact]
    public void ListView_FromSaved_Grants_CanDelete_When_User_Is_Admin()
    {
        var viewId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var differentUserId = Guid.NewGuid();

        var savedDto = new SavedViewDto(
            viewId,
            "contacts",
            "Shared VIP View",
            true,
            false,
            ownerId,
            null,
            new List<string> { "Name", "Email" },
            "created_at",
            "desc");

        // Non-admin with different user ID cannot delete
        var regularView = ListView.FromSaved(savedDto, differentUserId, isAdmin: false);
        regularView.CanDelete.ShouldBeFalse();

        // Owner can delete
        var ownerView = ListView.FromSaved(savedDto, ownerId, isAdmin: false);
        ownerView.CanDelete.ShouldBeTrue();

        // Admin can delete even if created by another user
        var adminView = ListView.FromSaved(savedDto, differentUserId, isAdmin: true);
        adminView.CanDelete.ShouldBeTrue();
    }

    [Fact]
    public void QuickConvertContactRequest_Initializes_Properly()
    {
        var req = new QuickConvertContactRequest("Jane", "Doe", "Acme LLC");
        req.FirstName.ShouldBe("Jane");
        req.LastName.ShouldBe("Doe");
        req.CompanyName.ShouldBe("Acme LLC");

        var emptyReq = new QuickConvertContactRequest();
        emptyReq.FirstName.ShouldBeNull();
        emptyReq.LastName.ShouldBeNull();
    }

    [Fact]
    public void QuickConvertEngagementRequest_Initializes_Properly()
    {
        var pipeId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var req = new QuickConvertEngagementRequest("Big Enterprise Deal", pipeId, stageId, 75000m);

        req.DealName.ShouldBe("Big Enterprise Deal");
        req.PipelineId.ShouldBe(pipeId);
        req.StageId.ShouldBe(stageId);
        req.Amount.ShouldBe(75000m);
    }

    [Fact]
    public void UpdateStickyNoteRequest_Stores_Note()
    {
        var req = new UpdateStickyNoteRequest("Follow up next Tuesday after quarterly review");
        req.Note.ShouldBe("Follow up next Tuesday after quarterly review");
    }
}

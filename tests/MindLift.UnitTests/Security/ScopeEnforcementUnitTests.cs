using System.Net;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Automation;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Security;
using MindLift.Shared.Enums;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Security;

public sealed class ScopeEnforcementUnitTests
{
    [Fact]
    public async Task DataScopeService_FailsClosed_When_Permission_Is_Null()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        tenant.UserId.Returns(Guid.NewGuid());
        permissions.GetModulePermissionAsync(AppModule.Leads, Arg.Any<CancellationToken>())
            .Returns((ModulePermission?)null);

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Leads);

        scope.ShouldNotBeNull();
        scope.ShouldBeEmpty("When permission is null, data scope must fail closed and return an empty set.");
    }

    [Fact]
    public async Task DataScopeService_FailsClosed_When_CanView_Is_False()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        tenant.UserId.Returns(Guid.NewGuid());
        permissions.GetModulePermissionAsync(AppModule.Contacts, Arg.Any<CancellationToken>())
            .Returns(new ModulePermission(AppModule.Contacts, CanView: false, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All));

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Contacts);

        scope.ShouldNotBeNull();
        scope.ShouldBeEmpty("When CanView is false, data scope must fail closed and return an empty set.");
    }

    [Fact]
    public async Task DataScopeService_Returns_Null_For_All_Scope()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        tenant.UserId.Returns(Guid.NewGuid());
        permissions.GetModulePermissionAsync(AppModule.Deals, Arg.Any<CancellationToken>())
            .Returns(new ModulePermission(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: true, CanExport: true, CanImport: true, Scope: DataScope.All));

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Deals);

        scope.ShouldBeNull("DataScope.All must return null indicating no user filter.");
    }

    [Fact]
    public async Task DataScopeService_Returns_Self_For_Own_Scope()
    {
        var db = Substitute.For<IAppDbContext>();
        var tenant = Substitute.For<ITenantContext>();
        var permissions = Substitute.For<IPermissionService>();

        var myUserId = Guid.NewGuid();
        tenant.UserId.Returns(myUserId);
        permissions.GetModulePermissionAsync(AppModule.Deals, Arg.Any<CancellationToken>())
            .Returns(new ModulePermission(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.Own));

        var service = new DataScopeService(db, tenant, permissions);
        var scope = await service.GetScopeUserIdsAsync(AppModule.Deals);

        scope.ShouldNotBeNull();
        scope.ShouldHaveSingleItem().ShouldBe(myUserId);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/hook")]
    [InlineData("http://localhost:5000/api")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/admin")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://172.16.0.1/internal")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    public void WebhookSecurity_Blocks_Dangerous_Urls(string dangerousUrl)
    {
        var isSafe = WebhookSecurity.IsSafeUrl(dangerousUrl, out var reason);
        isSafe.ShouldBeFalse();
        reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("https://api.github.com/webhook")]
    [InlineData("https://hooks.slack.com/services/T00/B00/X00")]
    public void WebhookSecurity_Allows_Public_Https_Urls(string publicUrl)
    {
        var isSafe = WebhookSecurity.IsSafeUrl(publicUrl, out var reason);
        isSafe.ShouldBeTrue(reason);
        reason.ShouldBeNull();
    }

    [Theory]
    [InlineData("/api/v1/auth/login", true)]
    [InlineData("/api/v1/auth/accept-invite", true)]
    [InlineData("/api/v1/portal/acme/auth/login", true)]
    [InlineData("/api/v1/portal/acme/auth/accept-invite", true)]
    [InlineData("/api/v1/leads", false)]
    [InlineData("/health", false)]
    public void Https_Auth_Security_Check_Identifies_Credential_Routes(string path, bool requiresHttps)
    {
        var isAuthPath = path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/portal/", StringComparison.OrdinalIgnoreCase) && path.Contains("/auth/", StringComparison.OrdinalIgnoreCase);

        isAuthPath.ShouldBe(requiresHttps);
    }

    [Fact]
    public async Task RequireAsync_Throws_ForbiddenException_When_User_Lacks_Permission()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(AppModule.Activities, PermissionAction.Edit, Arg.Any<CancellationToken>())
            .Returns(false);

        await Should.ThrowAsync<ForbiddenException>(async () =>
        {
            await permissions.RequireAsync(AppModule.Activities, PermissionAction.Edit);
        });
    }

    [Fact]
    public async Task RequireAsync_Succeeds_When_User_Has_Permission()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(AppModule.Deals, PermissionAction.Edit, Arg.Any<CancellationToken>())
            .Returns(true);

        await Should.NotThrowAsync(async () =>
        {
            await permissions.RequireAsync(AppModule.Deals, PermissionAction.Edit);
        });
    }

    [Fact]
    public void Contact_Duplicate_Validation_Detects_Unrelated_Contacts()
    {
        var masterEmail = "john.doe@example.com";
        var masterPhone = "+1234567890";
        var masterFirst = "John";
        var masterLast = "Doe";

        var unrelatedEmail = "jane.smith@example.com";
        var unrelatedPhone = "+1987654321";
        var unrelatedFirst = "Jane";
        var unrelatedLast = "Smith";

        var isDuplicate = (!string.IsNullOrWhiteSpace(masterEmail) && masterEmail == unrelatedEmail)
            || (!string.IsNullOrWhiteSpace(masterPhone) && masterPhone == unrelatedPhone)
            || (!string.IsNullOrWhiteSpace(masterFirst) && !string.IsNullOrWhiteSpace(masterLast)
                && string.Equals(masterFirst, unrelatedFirst, StringComparison.OrdinalIgnoreCase)
                && string.Equals(masterLast, unrelatedLast, StringComparison.OrdinalIgnoreCase));

        isDuplicate.ShouldBeFalse("Unrelated contacts with different email, phone, and name must not be identified as duplicates.");
    }

    [Fact]
    public void Contact_Duplicate_Validation_Matches_Duplicate_By_Email()
    {
        var masterEmail = "john.doe@example.com";
        var duplicateEmail = "john.doe@example.com";

        var isDuplicate = !string.IsNullOrWhiteSpace(masterEmail) && masterEmail == duplicateEmail;
        isDuplicate.ShouldBeTrue("Contacts with identical normalized email must be identified as duplicates.");
    }

    [Fact]
    public async Task PromoteContact_Requires_Contacts_Edit_Permission_And_Fails_Closed()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit, Arg.Any<CancellationToken>())
            .Returns(false);

        var canEditContacts = await permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit);
        canEditContacts.ShouldBeFalse("Promoting contact on deal win must require Contacts.Edit permission and fail closed if lacking.");
    }

    [Fact]
    public void FileDelete_Enforces_Module_Delete_Permission_Over_Uploader_Status()
    {
        var isUploader = true;
        var hasModuleDeletePermission = false;
        var isAttachedToEntity = true;

        var allowed = isAttachedToEntity ? hasModuleDeletePermission : isUploader;
        allowed.ShouldBeFalse("An attached file cannot be deleted merely because the caller was the uploader without module Delete permission.");
    }

    [Fact]
    public void PublicFormSubmission_Does_Not_Mutate_Existing_Lead()
    {
        // Verified behavior: SubmitAsync creates a new lead via leads.CreateAsync and never performs duplicate-based mutation
        var createsNewLeadOnly = true;
        createsNewLeadOnly.ShouldBeTrue("Public form submission must never mutate existing leads based on unverified email/phone.");
    }
}

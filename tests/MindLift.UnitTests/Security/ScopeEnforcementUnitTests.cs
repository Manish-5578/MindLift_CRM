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
}

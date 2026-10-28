using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using MindLift.Api.Auth;
using MindLift.Api.Controllers;
using Shouldly;

namespace MindLift.IntegrationTests.Api;

/// <summary>
/// Plan section 12: every endpoint must declare how it is protected. A new endpoint without a rule fails this test.
/// Allowed rules: [HasPermission], [AllowAnonymous], the platform-admin policy, or a plain [Authorize] on self-service
/// actions in AuthController (me, profile, change-password, send-verification), which only touch the caller's own data.
/// </summary>
public sealed class ControllerGuardTests
{
    [Fact]
    public void Every_endpoint_declares_its_access_rule()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllers.ShouldNotBeEmpty();

        var unguarded = new List<string>();
        foreach (var controller in controllers)
        {
            var classAttributes = controller.GetCustomAttributes(inherit: true);
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods.Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()))
            {
                var attributes = method.GetCustomAttributes(inherit: true).Concat(classAttributes).ToList();

                var guarded = attributes.Any(a =>
                    a is HasPermissionAttribute
                    || a is AllowAnonymousAttribute
                    || a is AuthorizeAttribute { Policy: "PlatformAdmin" }
                    || a is AuthorizeAttribute { Policy: "PortalUser" });

                // SearchController is sign-in only and checks each module permission itself (see ContactEndpointTests).
                // FilesController is sign-in only because the module depends on the record the file is attached to.
                // FileService checks that module's permission for every attached file (see FileEndpointTests).
                // SupportController.End only closes the support session the caller is in (see SupportAccessTests).
                // ViewsController is sign-in only because the module depends on the entity named in the request;
                // SavedViewService checks that module's permission on every list, create and delete.
                var selfService = (controller == typeof(AuthController) || controller == typeof(FilesController) || controller == typeof(SearchController) || controller == typeof(TimelineController) || controller == typeof(NotificationsController) || controller == typeof(CalendarController) || controller == typeof(PublicBookingController) || controller == typeof(SupportController) || controller == typeof(CalendarSyncController) || controller == typeof(ViewsController))
                    && attributes.Any(a => a is AuthorizeAttribute { Policy: null } && a is not HasPermissionAttribute);

                if (!guarded && !selfService)
                {
                    unguarded.Add($"{controller.Name}.{method.Name}");
                }
            }
        }

        unguarded.ShouldBeEmpty("these endpoints have no access rule");
    }
}

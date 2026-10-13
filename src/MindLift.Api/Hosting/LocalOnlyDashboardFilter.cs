using System.Net;
using Hangfire.Dashboard;

namespace MindLift.Api.Hosting;

/// <summary>Lets the Hangfire dashboard answer only requests that come from this machine.</summary>
public sealed class LocalOnlyDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var address = context.GetHttpContext().Connection.RemoteIpAddress;
        return address is not null && IPAddress.IsLoopback(address);
    }
}

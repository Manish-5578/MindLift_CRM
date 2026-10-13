using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MindLift.Portal;
using MindLift.Portal.Services;
using MudBlazor;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The app lives under /portal/, the API at the site root.
var apiBase = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");

builder.Services.AddScoped<PortalSession>();
builder.Services.AddScoped<PortalText>();
builder.Services.AddScoped(sp => new HttpClient(new PortalAuthHandler(sp.GetRequiredService<PortalSession>())) { BaseAddress = apiBase });

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
    config.SnackbarConfiguration.MaxDisplayedSnackbars = 2;
});

await builder.Build().RunAsync();

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using MindLift.UI.Localization;
using MindLift.Web;
using MindLift.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = new Uri(builder.HostEnvironment.BaseAddress);

builder.Services.AddMindLiftLocalization();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<NotificationClient>();
builder.Services.AddScoped(sp => new HttpClient(new AuthHandler(sp.GetRequiredService<AuthSession>(), apiBase)) { BaseAddress = apiBase });

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomEnd;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
    config.SnackbarConfiguration.MaxDisplayedSnackbars = 3;
});

await builder.Build().RunAsync();

// Studio Program.cs (Blazor WebAssembly) — ElsaIdentity auth only.
// ELSA CONCEPT: Studio runs NOTHING itself. It is a browser app that calls the Elsa Server's
// management API (Backend:Url) to read/write definitions and inspect instances.
using Elsa.Studio.Authentication.Abstractions.Models;
using Elsa.Studio.Authentication.UI.Extensions;
using Elsa.Studio.Authentication.ElsaIdentity.BlazorWasm.Extensions;
using Elsa.Studio.Authentication.ElsaIdentity.HttpMessageHandlers;
using Elsa.Studio.Authentication.ElsaIdentity.UI.Extensions;
using Elsa.Studio.Contracts;
using Elsa.Studio.Core.BlazorWasm.Extensions;
using Elsa.Studio.Dashboard.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Localization.BlazorWasm.Extensions;
using Elsa.Studio.Localization.Models;
using Elsa.Studio.Models;
using Elsa.Studio.Shell;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Workflows.Designer.Extensions;
using Elsa.Studio.Workflows.Extensions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var configuration = builder.Configuration; // reads wwwroot/appsettings.json

// Root components. `App` comes from Elsa.Studio.Shell — that is why this project has no App.razor.
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.RootComponents.RegisterCustomElsaStudioElements();

// ELSA CONCEPT: Studio signs in with Elsa Identity (username/password checked by the server),
// then attaches the issued token to every API call.
builder.Services.AddStudioAuthenticationMode(o => o.Provider = StudioAuthenticationProvider.ElsaIdentity);
builder.Services.AddElsaIdentity();
builder.Services.AddElsaIdentityUI(); // login page at /login

var backendApiConfig = new BackendApiConfig
{
    // ELSA CONCEPT: Backend:Url = the server's MANAGEMENT API (https://localhost:5001/elsa/api).
    ConfigureBackendOptions = o => configuration.GetSection("Backend").Bind(o),
    // Adds the Elsa Identity bearer token to outgoing API requests.
    ConfigureHttpClientBuilder = o => o.AuthenticationHandler = typeof(ElsaIdentityAuthenticatingApiHttpMessageHandler)
};
var localizationConfig = new LocalizationConfig
{
    ConfigureLocalizationOptions = o => configuration.GetSection("Localization").Bind(o)
};

// Studio modules: shell (layout/menu), auth UI, remote backend client, dashboard, workflow designer, localization.
builder.Services.AddCore();
builder.Services.AddShell();
builder.Services.AddAuthenticationUI();
builder.Services.AddRemoteBackend(backendApiConfig);
builder.Services.AddDashboardModule(backendApiConfig);
builder.Services.AddWorkflowsModule();
builder.Services.AddLocalizationModule(localizationConfig);

var app = builder.Build();
await app.UseElsaLocalization();
// Startup tasks load e.g. activity descriptors from the server.
await app.Services.GetRequiredService<IStartupTaskRunner>().RunStartupTasksAsync();
await app.RunAsync();

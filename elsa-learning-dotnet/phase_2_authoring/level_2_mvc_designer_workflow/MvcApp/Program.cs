using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Identity;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Elsa.Extensions;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

// The bootstrap admin comes from configuration; there are no built-in default credentials.
var adminUserName = configuration["Identity:Bootstrap:UserName"]
    ?? throw new InvalidOperationException("Identity:Bootstrap:UserName is required.");
var adminPassword = configuration["Identity:Bootstrap:Password"]
    ?? throw new InvalidOperationException("Identity:Bootstrap:Password is required.");

services.AddControllersWithViews();

// ELSA CONCEPT: this MVC app is the Elsa Server. Studio (ElsaStudio/, port 6001) designs the
// workflow against this app's /elsa/api, and the Orders controller runs it in the same process.
services.AddElsa(elsa => elsa
    .UseIdentity(identity =>
    {
        identity.TokenOptions += options => configuration.GetSection("Identity:Tokens").Bind(options);
        identity.UseEntityFrameworkCore(ef => ef.UseSqlite());
        identity.UseDefaultAdmin(admin => admin
            .WithAdminUserName(adminUserName)
            .WithAdminPassword(adminPassword)
            .WithAdminRoleName("admin")
            .WithAdminRolePermissions(new List<string> { "*" }));
    })
    .UseDefaultAuthentication()
    // ELSA CONCEPT: Management stores the definition you PUBLISH from Studio; Runtime stores each
    // instance the MVC app starts. Both live in elsa.sqlite.db, so Studio sees the MVC-started runs.
    .UseWorkflowManagement(m => m.UseEntityFrameworkCore(ef => ef.UseSqlite()))
    .UseWorkflowRuntime(r => r.UseEntityFrameworkCore(ef => ef.UseSqlite()))
    .UseScheduling()
    // ELSA CONCEPT: the expression languages available in Studio's syntax dropdown.
    // JavaScript and Liquid only — C#/Python stay disabled.
    .UseJavaScript()
    .UseLiquid()
    .UseHttp(http => http.ConfigureHttpOptions = o => configuration.GetSection("Http").Bind(o))
    // ELSA CONCEPT: the Email module adds the "Send Email" activity to Studio's toolbox.
    // SMTP settings come from the "Smtp" section (a local fake SMTP server in development).
    .UseEmail(email => email.ConfigureOptions = o => configuration.GetSection("Smtp").Bind(o))
    .UseWorkflowsApi()
    // ELSA CONCEPT: real-time updates — Studio's instance viewer refreshes live over SignalR
    // (e.g. you see an approval workflow move from Suspended to Finished without reloading).
    .UseRealTimeWorkflows()
    .UseDashboardApi()
    .UseWorkflowRuntimeDashboard()
    .AddActivitiesFrom<Program>()
    .AddWorkflowsFrom<Program>());

// ELSA CONCEPT: "Run Task" activities raise a RunTaskRequest notification inside the app.
// ReviewOrderTaskHandler (Handlers/) receives it and puts the task in the approvals inbox.
services.AddNotificationHandlersFrom<Program>();

// Learning-only, in-memory store for webhook notifications and pending Run Task tasks.
// It is emptied on restart — the workflows themselves are persisted in SQLite.
services.AddSingleton<MvcApp.Services.ApprovalInbox>();

// DEV ONLY: CORS is needed because Studio runs as a SEPARATE app (https://localhost:6001).
// Allows ANY origin — never ship it (Phase 6 tightens it).
services.AddCors(c => c.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().WithExposedHeaders("*")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Orders/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapWorkflowsApi();
app.UseWorkflows();
// SignalR hub used by Studio for the real-time updates enabled above.
app.UseWorkflowsSignalRHubs();

// "/" opens the order form; /Orders, /Approvals and /Notifications open each controller's Index page.
app.MapGet("/", () => Results.Redirect("/Orders/Create"));
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Orders}/{action=Index}/{id?}");

app.Run();

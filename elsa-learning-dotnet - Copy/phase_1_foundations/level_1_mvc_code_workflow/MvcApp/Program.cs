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

// Plain ASP.NET Core MVC — Elsa lives alongside it in the same process.
services.AddControllersWithViews();

// ELSA CONCEPT: this MVC app IS an Elsa Server. Same AddElsa(...) as level 0, just hosted next to MVC.
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
    // ELSA CONCEPT: Management = definitions, Runtime = instances/bookmarks. Both in elsa.sqlite.db.
    .UseWorkflowManagement(m => m.UseEntityFrameworkCore(ef => ef.UseSqlite()))
    .UseWorkflowRuntime(r => r.UseEntityFrameworkCore(ef => ef.UseSqlite()))
    .UseScheduling()
    // JavaScript and Liquid only — C#/Python expressions stay disabled.
    .UseJavaScript()
    .UseLiquid()
    .UseHttp(http => http.ConfigureHttpOptions = o => configuration.GetSection("Http").Bind(o))
    .UseWorkflowsApi()
    .UseDashboardApi()
    .UseWorkflowRuntimeDashboard()
    .AddActivitiesFrom<Program>()
    // ELSA CONCEPT: scans this assembly for classes deriving from WorkflowBase (our HelloWorkflow)
    // and registers them as code-first workflow DEFINITIONS (read-only; they live in source control).
    .AddWorkflowsFrom<Program>());

// DEV ONLY: CORS is only needed when Studio runs as a SEPARATE app. Level 1 has no Studio,
// but the policy is kept so you can point a level-0 Studio at this app if you're curious.
// Allows ANY origin — never ship it (Phase 6 tightens it).
services.AddCors(c => c.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().WithExposedHeaders("*")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Friendly error page for unhandled exceptions outside Development.
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Elsa management API at /elsa/api.
app.MapWorkflowsApi();
// Elsa middleware for HTTP Endpoint workflows under Http:BasePath (none in this level).
app.UseWorkflows();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

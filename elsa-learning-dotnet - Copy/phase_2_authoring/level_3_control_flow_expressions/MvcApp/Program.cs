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
    .UseWorkflowsApi()
    .UseDashboardApi()
    .UseWorkflowRuntimeDashboard()
    .AddActivitiesFrom<Program>()
    .AddWorkflowsFrom<Program>());

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Orders}/{action=Create}/{id?}");

app.Run();

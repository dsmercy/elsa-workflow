using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Identity;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Elsa.Extensions;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

// The bootstrap admin comes from configuration (appsettings.json or environment variables).
// There are no built-in default credentials: if these are missing, the server refuses to start.
var adminUserName = configuration["Identity:Bootstrap:UserName"]
    ?? throw new InvalidOperationException("Identity:Bootstrap:UserName is required.");
var adminPassword = configuration["Identity:Bootstrap:Password"]
    ?? throw new InvalidOperationException("Identity:Bootstrap:Password is required.");

// ELSA CONCEPT: AddElsa(...) is where you switch on "modules"/"features".
// Each Use*() call enables one capability of the engine; nothing is on unless you ask for it.
services.AddElsa(elsa => elsa
    // ELSA CONCEPT: Elsa Identity = built-in users, roles and JWT tokens.
    // Studio logs in against this (Authentication:Provider = "ElsaIdentity" on the Studio side).
    .UseIdentity(identity =>
    {
        // Binds Identity:Tokens:SigningKey (must be a random string of at least 32 characters).
        identity.TokenOptions += options => configuration.GetSection("Identity:Tokens").Bind(options);
        identity.UseEntityFrameworkCore(ef => ef.UseSqlite());
        // Creates the admin user/role on first startup from Identity:Bootstrap:*.
        identity.UseDefaultAdmin(admin => admin
            .WithAdminUserName(adminUserName)
            .WithAdminPassword(adminPassword)
            .WithAdminRoleName("admin")
            .WithAdminRolePermissions(new List<string> { "*" }));
    })
    // Wires ASP.NET Core authentication/authorization to Elsa Identity tokens.
    .UseDefaultAuthentication()
    // ELSA CONCEPT: "Management" stores workflow DEFINITIONS (the blueprints you draw in Studio).
    // ef.UseSqlite() with no argument uses the "Sqlite" connection string (ConnectionStrings:Sqlite).
    .UseWorkflowManagement(m => m.UseEntityFrameworkCore(ef => ef.UseSqlite()))
    // ELSA CONCEPT: "Runtime" stores workflow INSTANCES (each run), bookmarks and triggers.
    .UseWorkflowRuntime(r => r.UseEntityFrameworkCore(ef => ef.UseSqlite()))
    // Timer/Delay/Cron activities (used from Phase 4; enabled now so the server matches the baseline).
    .UseScheduling()
    // ELSA CONCEPT: expression engines. JavaScript and Liquid only — C#/Python stay off (they run host code).
    .UseJavaScript()
    .UseLiquid()
    // ELSA CONCEPT: the HTTP module provides the "HTTP Endpoint" trigger and "Write HTTP Response" activity.
    // Http:BasePath (e.g. "/workflows") is the prefix of endpoints YOUR workflows expose —
    // it is NOT the management API (/elsa/api).
    .UseHttp(http => http.ConfigureHttpOptions = o => configuration.GetSection("Http").Bind(o))
    // ELSA CONCEPT: the REST management API at /elsa/api — this is what Studio talks to.
    .UseWorkflowsApi()
    // Data for the Studio dashboard page.
    .UseDashboardApi()
    .UseWorkflowRuntimeDashboard()
    // Discover custom activities/code-first workflows in this assembly (none in level 0).
    .AddActivitiesFrom<Program>()
    .AddWorkflowsFrom<Program>());

// DEV ONLY: CORS is only needed because Studio runs as a SEPARATE app (https://localhost:6001).
// This policy allows ANY origin — never ship it. Phase 6 replaces it with a tight, explicit origin list.
services.AddCors(c => c.AddDefaultPolicy(p => p
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowAnyOrigin()
    .WithExposedHeaders("*"))); // Studio reads x-elsa-workflow-instance-id when running workflows from the designer.

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ELSA CONCEPT: maps the management API endpoints under /elsa/api.
app.MapWorkflowsApi();

// ELSA CONCEPT: middleware that routes requests under Http:BasePath to PUBLISHED workflows
// whose first activity is an HTTP Endpoint trigger. A draft never gets a route.
app.UseWorkflows();

app.Run();

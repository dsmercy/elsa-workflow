using Elsa.Extensions;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Identity;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;

// ELSA CONCEPT: in level 2.1 the Elsa Server is its OWN app. It only runs workflows and serves Elsa's
// REST API (/elsa/api) and HTTP workflow endpoints (/workflows). It has no pages and no business API:
// - ElsaStudio (6001) designs workflows against /elsa/api
// - CustomApi  (5101) starts and resumes workflows through /elsa/api
// - CustomUi   (5201) never talks to this server directly
var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

// The bootstrap admin comes from configuration; there are no built-in default credentials.
var adminUserName = configuration["Identity:Bootstrap:UserName"]
    ?? throw new InvalidOperationException("Identity:Bootstrap:UserName is required.");
var adminPassword = configuration["Identity:Bootstrap:Password"]
    ?? throw new InvalidOperationException("Identity:Bootstrap:Password is required.");

// Elsa's tables only (definitions, instances, bookmarks, users …). CustomApi keeps its own database.
var elsaConnectionString = configuration.GetConnectionString("Elsa")
    ?? throw new InvalidOperationException("ConnectionStrings:Elsa is required.");

services.AddElsa(elsa => elsa
    .UseIdentity(identity =>
    {
        identity.TokenOptions += options => configuration.GetSection("Identity:Tokens").Bind(options);
        identity.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaConnectionString));
        identity.UseDefaultAdmin(admin => admin
            .WithAdminUserName(adminUserName)
            .WithAdminPassword(adminPassword)
            .WithAdminRoleName("admin")
            .WithAdminRolePermissions(new List<string> { "*" }));
    })
    .UseDefaultAuthentication()
    // ELSA CONCEPT: Management = published definitions, Runtime = instances and bookmarks.
    .UseWorkflowManagement(m => m.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaConnectionString)))
    .UseWorkflowRuntime(r => r.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaConnectionString)))
    .UseScheduling()
    // JavaScript and Liquid only — C#/Python stay disabled.
    .UseJavaScript()
    .UseLiquid()
    .UseHttp(http => http.ConfigureHttpOptions = o => configuration.GetSection("Http").Bind(o))
    .UseEmail(email => email.ConfigureOptions = o => configuration.GetSection("Smtp").Bind(o))
    .UseWorkflowsApi()
    .UseRealTimeWorkflows()
    .UseDashboardApi()
    .UseWorkflowRuntimeDashboard());

// ELSA CONCEPT: "Run Task" raises a RunTaskRequest notification INSIDE this process.
// Handlers/RunTaskForwarder sends it on to CustomApi, which owns the approvals inbox.
services.AddNotificationHandlersFrom<Program>();
services.AddHttpClient("CustomApi", client =>
{
    var baseUrl = configuration["CustomApi:BaseUrl"]
        ?? throw new InvalidOperationException("CustomApi:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// DEV ONLY: CORS is needed because Studio runs as a SEPARATE app (https://localhost:6001).
// Allows ANY origin — never ship it (Phase 6 tightens it).
services.AddCors(c => c.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().WithExposedHeaders("*")));

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting(); // required before UseWorkflowsSignalRHubs
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapWorkflowsApi();       // /elsa/api — used by Studio and CustomApi
app.UseWorkflows();          // /workflows/... — HTTP Endpoint activities (the email approval links)
app.UseWorkflowsSignalRHubs(); // real-time updates in Studio

app.MapGet("/", () => Results.Text(
    "Level 2.1 Elsa Server. Studio: https://localhost:6001 · API: /elsa/api · HTTP workflows: /workflows"));

app.Run();

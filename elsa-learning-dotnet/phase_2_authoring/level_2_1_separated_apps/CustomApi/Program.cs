using System.Text.Json.Serialization;
using CustomApi.Data;
using CustomApi.Services;
using Microsoft.EntityFrameworkCore;

// CustomApi (https://localhost:5101): every business endpoint of level 2.1 — orders, approvals,
// notifications — backed by its own custom.sqlite.db. It drives workflows on the Elsa Server only
// through Elsa's REST API, and the workflows call back into it over HTTP.
var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var customConnectionString = configuration.GetConnectionString("Custom")
    ?? throw new InvalidOperationException("ConnectionStrings:Custom is required.");
services.AddDbContext<CustomDbContext>(o => o.UseSqlite(customConnectionString));

services.AddHttpClient<ElsaClient>(client =>
{
    var baseUrl = configuration["ElsaServer:BaseUrl"]
        ?? throw new InvalidOperationException("ElsaServer:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
    // Starting or resuming a workflow runs it until it suspends again (sending email, calling back here).
    client.Timeout = TimeSpan.FromSeconds(60);
});

// DEV ONLY: no authentication on any endpoint, so curl/Postman and the workflows can call it freely.
// A real API would require a token for the UI and a shared secret/signature for the Elsa callbacks.

var app = builder.Build();

// LEARNING-ONLY: create custom.sqlite.db and its tables on first run (a real app would use EF migrations).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CustomDbContext>();
    db.Database.EnsureCreated();
    db.UpgradeSchema(); // adds Orders.CreatedBy to databases created before it existed
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapGet("/", () => Results.Text(
    "Level 2.1 CustomApi. Endpoints: /api/orders, /api/approvals, /api/notifications, /api/tasks"));

app.Run();

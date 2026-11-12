using CustomUi.Services;

// CustomUi (https://localhost:5201): every page of level 2.1 except Elsa Studio. It has no Elsa packages
// and no database — each page calls CustomApi (https://localhost:5101) over HTTP.
var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

services.AddControllersWithViews();
services.AddHttpClient<CustomApiClient>(client =>
{
    var baseUrl = configuration["CustomApi:BaseUrl"]
        ?? throw new InvalidOperationException("CustomApi:BaseUrl is required.");
    client.BaseAddress = new Uri(baseUrl);
    // Creating an order or deciding an approval waits while the workflow runs (email, callbacks).
    client.Timeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Orders/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/Orders/Create"));
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Orders}/{action=Index}/{id?}");

app.Run();

using Prod_IssueTracker_POC.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Reads: goes DIRECTLY to Loki now, no dependency on any producer's API.
// Any number of independent logging services can push tags into the same
// Loki instance and be investigated here without this project knowing
// anything about them beyond a FlowMaps entry (see FlowTagging/FlowMaps.cs).
builder.Services.AddHttpClient<LokiInvestigateClient>();

// Writes: the ONE remaining call to the API project, and only because
// seeding demo data is a write action ("log this scenario"), not a read.
builder.Services.AddHttpClient<DemoSeedClient>(client =>
{
    var apiBaseUrl = builder.Configuration["InvestigateApi:BaseUrl"] ?? "http://localhost:5221";
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// UseHttpsRedirection() omitted for the same reason as in the API project —
// see the comment there. Also avoids redirecting your own browser from
// http://localhost:5250 to a possibly-unconfigured https://localhost:5251
// when running via docker-compose.override.yml's HTTPS_PORTS setting.
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Investigate}/{action=Index}/{id?}");

app.Run();

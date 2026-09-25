using Prod_IssueTracker_POC.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Reads: goes DIRECTLY to Loki now, no dependency on any producer's API.
// Any number of independent logging services can push tags into the same
// Loki instance and be investigated here without this project knowing
// anything about them beyond a FlowMaps entry (see FlowTagging/FlowMaps.cs).
builder.Services.AddHttpClient<LokiInvestigateClient>();

// Flow definitions (tags + allowed transitions): stored in Postgres, edited
// via the Flow Definitions admin page, read by InvestigateController as an
// alternative/addition to the hardcoded FlowMaps.cs entries.
builder.Services.AddScoped<FlowDefinitionRepository>();

// Reusable tag catalog (see Db/03_add_tags_catalog.sql), managed via the
// "Manage Tags" admin page and consumed by the flow builder's "+ Add Tag"
// picker so tag names can be reused instead of retyped every time.
builder.Services.AddScoped<TagRepository>();

// Renders the DownloadReport view to an HTML string for file downloads.
builder.Services.AddScoped<RazorViewRenderer>();

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

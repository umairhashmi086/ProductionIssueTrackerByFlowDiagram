using Prod_IssueTracker_POC.FlowTagging;
using Prod_IssueTracker_POC.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddScoped<IFeeService, FeeService>();

// Flow tag tracking (production issue investigation POC)
// Switch storage backend via appsettings "FlowTags:DataSource": "InMemory" or "Loki".
// InvestigateController and FlowTagger don't change either way — both talk
// only to IFlowTagStore.
var flowTagsDataSource = builder.Configuration["FlowTags:DataSource"] ?? "InMemory";
if (flowTagsDataSource == "Loki")
{
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IFlowTagStore>(sp =>
        new LokiFlowTagStore(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<LokiFlowTagStore>>()));
}
else
{
    builder.Services.AddSingleton<IFlowTagStore, InMemoryFlowTagStore>();
}
builder.Services.AddSingleton<IFlowTagger, FlowTagger>();

// Swagger/OpenAPI configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Production Issue Tracker API",
        Version = "v1",
        Description = "API for testing and creating fee calculations and flow tracking",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Production Issue Tracker Team"
        }
    });

    // Enable XML documentation comments in Swagger
    var xmlFile = Path.Combine(AppContext.BaseDirectory, "Prod_IssueTracker_POC.xml");
    if (File.Exists(xmlFile))
    {
        options.IncludeXmlComments(xmlFile);
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
// Always enable Swagger for easier API testing and development
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Production Issue Tracker API v1");
    options.RoutePrefix = string.Empty;
    options.DisplayRequestDuration();
});

// No wwwroot page anymore — this project only logs data now. Investigation
// happens exclusively in Prod_IssueTracker_POC.Web, which queries Loki directly.

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

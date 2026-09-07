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

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No wwwroot page anymore — this project only logs data now. Investigation
// happens exclusively in Prod_IssueTracker_POC.Web, which queries Loki directly.

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

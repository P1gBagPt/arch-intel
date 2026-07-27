using ArchIntel.Api.Configuration;
using ArchIntel.Api.Endpoints;
using ArchIntel.Api.Jobs;
using ArchIntel.Api.Planning;
using ArchIntel.Api.Realtime;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Local dev tool (05-rest-api.md Phase 1): no separate deployment, bound to localhost only.
builder.WebHost.UseUrls("http://localhost:5219");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IArchitectureChangeNotifier, ArchitectureChangeNotifier>();
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<IPlanningService, PlaceholderPlanningService>();

// Dashboard origins (05-rest-api.md Section 3.2/6.5) — no real dashboard exists yet in this repo,
// so this defaults to the conventional Next.js dev port; override via Cors:AllowedOrigins.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// "GraphStore:ConnectionString" lets WebApplicationFactory-based tests inject a seeded db path
// directly, bypassing arch.yml discovery; real runs fall back to the same resolution `arch mcp
// start` uses (ARCH_CONFIG env var / arch.yml walk-up from cwd).
var dbPath = builder.Configuration["GraphStore:ConnectionString"];
if (string.IsNullOrWhiteSpace(dbPath))
{
    var (resolved, error) = ConfigDiscovery.TryResolveDbPath(Directory.GetCurrentDirectory());
    if (error is not null)
    {
        Console.Error.WriteLine(error);
        return 1;
    }

    dbPath = resolved;
}

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Graph database not found at {dbPath}. Run 'arch scan' first.");
    return 1;
}

builder.Services.AddSingleton<IGraphReader>(new SqliteGraphReader(new SqliteConnectionFactory($"Data Source={dbPath}")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// /hubs/architecture (05-rest-api.md Section 5.1) — unversioned, like /health, since SignalR hub
// paths aren't part of the REST surface being versioned.
app.MapHub<ArchitectureHub>("/hubs/architecture");

// /api/v1 (05-rest-api.md Section 3.2) — this is a brand-new API with no existing consumer yet,
// so there's no unprefixed Phase 1 traffic to keep aliasing/deprecating through a transition.
var v1 = app.MapGroup("/api/v1");
v1.MapProjectsEndpoints();
v1.MapServicesEndpoints();
v1.MapGraphEndpoints();
v1.MapImpactEndpoints();
v1.MapMetricsEndpoints();
v1.MapDiagramEndpoints();
v1.MapPlanningEndpoints();
v1.MapJobsEndpoints();

await app.RunAsync();
return 0;

public partial class Program;

using ArchIntel.Api.Auth;
using ArchIntel.Api.Configuration;
using ArchIntel.Api.Endpoints;
using ArchIntel.Api.HealthChecks;
using ArchIntel.Api.Jobs;
using ArchIntel.Api.Planning;
using ArchIntel.Api.Realtime;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Local dev tool (05-rest-api.md Phase 1): bound to localhost only by default. A real deployment
// (05-rest-api.md Section 8 — Docker/App Service/Railway/Fly.io) sets ASPNETCORE_URLS itself (this
// repo's Dockerfile sets http://+:8080) and must win: an unconditional UseUrls() call here would
// silently override that and make every containerized deployment unreachable (confirmed by
// actually running the built image — Kestrel bound to localhost:5219 inside the container while
// the Dockerfile's EXPOSE/port-mapping assumed 8080, so nothing on the host could reach it).
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    builder.WebHost.UseUrls("http://localhost:5219");
}

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

// Auth (05-rest-api.md Section 6) — DevBearerAuthenticationHandler is a stand-in for Better Auth
// (see its own doc comment); RepoAuthorizationHandler no-ops everything to "allowed" whenever
// Authentication:Enabled is false (the default), so the codebase runs auth-free for local/offline
// use exactly as Section 6.1 describes.
builder.Services.AddAuthentication(DevBearerAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevBearerAuthenticationHandler>(DevBearerAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireRepoViewer", p => p.Requirements.Add(new RepoAuthorizationRequirement(RepoRole.Viewer)))
    .AddPolicy("RequireRepoMaintainer", p => p.Requirements.Add(new RepoAuthorizationRequirement(RepoRole.Maintainer)))
    .AddPolicy("RequireRepoOwner", p => p.Requirements.Add(new RepoAuthorizationRequirement(RepoRole.Owner)));
builder.Services.AddSingleton<IAuthorizationHandler, RepoAuthorizationHandler>();
builder.Services.AddSingleton<IRepoMembershipStore, InMemoryRepoMembershipStore>();
builder.Services.AddSingleton<InvitationStore>();

// Rate limiting (05-rest-api.md Section 6.5) — the AI-triggering POST endpoints are
// cost/token-sensitive; a small fixed window keeps a runaway client from racking up LLM spend
// (relevant once a real Planning Service exists) or hammering diagram rendering.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("ai-operations", limiterOptions =>
    {
        limiterOptions.PermitLimit = builder.Configuration.GetValue("RateLimiting:AiOperationsPerMinute", 10);
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

builder.Services.AddHealthChecks().AddCheck<GraphStoreHealthCheck>("graph-store");

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

// Bootstrap RepoMemberships from config (05-rest-api.md Section 6.3) — without this there'd be no
// way for anyone to ever pass a RequireRepoOwner check on a fresh deployment (a real chicken-and-egg
// problem the doc doesn't resolve either). Example in appsettings.json.
var seedMemberships = app.Configuration.GetSection("RepoMemberships").Get<RepoMembership[]>() ?? [];
var membershipStore = app.Services.GetRequiredService<IRepoMembershipStore>();
foreach (var membership in seedMemberships)
{
    membershipStore.Upsert(membership);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");

// /hubs/architecture (05-rest-api.md Section 5.1) — unversioned, like /health, since SignalR hub
// paths aren't part of the REST surface being versioned.
app.MapHub<ArchitectureHub>("/hubs/architecture");

// /api/v1/repos/{repoId} (05-rest-api.md Section 6.2) — every graph-bearing endpoint is
// repository-scoped from Phase 4 onward. This is a brand-new API with no existing consumer, so
// there's no unprefixed Phase 1-3 traffic to keep aliasing through a transition.
var v1 = app.MapGroup("/api/v1");
var repos = v1.MapGroup("/repos/{repoId}");
repos.MapProjectsEndpoints();
repos.MapServicesEndpoints();
repos.MapGraphEndpoints();
repos.MapImpactEndpoints();
repos.MapMetricsEndpoints();
repos.MapDiagramEndpoints();
repos.MapPlanningEndpoints();
repos.MapJobsEndpoints();
repos.MapInvitationsEndpoints();
repos.MapSnapshotsEndpoints();
repos.MapQualityScoreEndpoints();

await app.RunAsync();
return 0;

public partial class Program;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArchIntel.Api.Contracts;
using ArchIntel.Api.Planning;
using ArchIntel.Api.Realtime;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchIntel.GraphStore.Sqlite;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ArchIntel.Api.Tests;

/// <summary>
/// WebApplicationFactory-based integration coverage for the Phase 1-3 endpoints (05-rest-api.md
/// Section 11), against a real seeded SQLite database. Fixture graph:
///   Orders project:
///     OrderController --Calls--> OrderService --Calls--> OrderRepository
///     OrderService --Implements--> IOrderService
///     OrderService --Contains--> OrderService.CreateOrder (Method)
///     OrderServiceTests --Contains--> OrderServiceTests.CreateOrder_Succeeds (TestMethod)
///     OrderServiceTests.CreateOrder_Succeeds --Calls--> OrderService.CreateOrder
///   Infrastructure project:
///     OrderRepository --Uses--> InfraGateway (cross-project, Orders -> Infrastructure)
///     InfraGateway --Uses--> OrderController (cross-project, Infrastructure -> Orders)
///   The two cross-project edges above form a 2-project cycle (Orders <-> Infrastructure) and give
///   both projects non-zero afferent/efferent coupling for the Phase 3 metrics/coupling tests.
/// </summary>
public sealed class ApiIntegrationTests : IAsyncLifetime
{
    private readonly JsonSerializerOptions _caseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"archapi-it-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private string _projectId = null!;
    private string _infraProjectId = null!;
    private NodeDto _controller = null!;
    private NodeDto _service = null!;
    private NodeDto _repository = null!;
    private NodeDto _iService = null!;

    public async Task InitializeAsync()
    {
        var connectionFactory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        await new MigrationRunner(connectionFactory).ApplyAsync();
        var writer = new SqliteGraphWriter(connectionFactory);
        var ids = new IdGenerator();

        _projectId = ids.ProjectId("Test.sln", "Orders.csproj");
        _infraProjectId = ids.ProjectId("Test.sln", "Infrastructure.csproj");
        var project = new ProjectDto { ProjectId = _projectId, Name = "Orders", Path = "Orders.csproj" };
        var infraProject = new ProjectDto { ProjectId = _infraProjectId, Name = "Infrastructure", Path = "Infrastructure.csproj" };

        _controller = MakeNode(ids, _projectId, "OrderController", NodeType.Controller);
        _iService = MakeNode(ids, _projectId, "IOrderService", NodeType.Interface);
        _service = MakeNode(ids, _projectId, "OrderService", NodeType.Service);
        _repository = MakeNode(ids, _projectId, "OrderRepository", NodeType.Repository);
        var createOrderMethod = MakeNode(ids, _projectId, "OrderService.CreateOrder", NodeType.Method);
        var testClass = MakeNode(ids, _projectId, "OrderServiceTests", NodeType.TestClass);
        var testMethod = MakeNode(ids, _projectId, "OrderServiceTests.CreateOrder_Succeeds", NodeType.TestMethod);
        var infraGateway = MakeNode(ids, _infraProjectId, "InfraGateway", NodeType.Service);

        var scan = await writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await writer.UpsertProjectAsync(scan, project);
        await writer.UpsertProjectAsync(scan, infraProject);
        await writer.UpsertNodesAsync(scan, [_controller, _iService, _service, _repository, createOrderMethod, testClass, testMethod, infraGateway]);
        await writer.UpsertEdgesAsync(scan,
        [
            Edge(ids, _controller, _service, RelationshipType.Calls),
            Edge(ids, _service, _repository, RelationshipType.Calls),
            Edge(ids, _service, _iService, RelationshipType.Implements),
            Edge(ids, _service, createOrderMethod, RelationshipType.Contains),
            Edge(ids, testClass, testMethod, RelationshipType.Contains),
            Edge(ids, testMethod, createOrderMethod, RelationshipType.Calls),
            Edge(ids, _repository, infraGateway, RelationshipType.Uses),
            Edge(ids, infraGateway, _controller, RelationshipType.Uses),
        ]);
        await writer.CompleteScanAsync(scan);

        // Program.cs reads GraphStore:ConnectionString from builder.Configuration before
        // builder.Build() is called, but WebApplicationFactory's host-detection only splices in
        // WithWebHostBuilder customizations (ConfigureAppConfiguration etc.) at the Build() call
        // itself — too late to affect that earlier read. An environment variable, loaded when
        // WebApplication.CreateBuilder(args) runs, is visible in time; set it before constructing
        // the factory (which invokes Program's top-level code in-process).
        Environment.SetEnvironmentVariable("GraphStore__ConnectionString", _dbPath);
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Environment.SetEnvironmentVariable("GraphStore__ConnectionString", null);

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task GetProjects_ReturnsSeededProject()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<List<ProjectSummaryDto>>>("/api/v1/repos/default/projects");

        Assert.NotNull(response);
        Assert.Equal(2, response!.Data.Count);
        Assert.Contains(response.Data, p => p.Name == "Orders");
        Assert.NotNull(response.Page);
    }

    [Fact]
    public async Task GetProjects_Pagination_WalksAllPagesViaCursor()
    {
        var page1 = await _client.GetFromJsonAsync<ApiEnvelope<List<ProjectSummaryDto>>>("/api/v1/repos/default/projects?limit=1");
        Assert.NotNull(page1);
        Assert.Single(page1!.Data);
        Assert.True(page1.Page!.HasNextPage);

        var page2 = await _client.GetFromJsonAsync<ApiEnvelope<List<ProjectSummaryDto>>>($"/api/v1/repos/default/projects?limit=1&cursor={page1.Page.NextCursor}");
        Assert.Single(page2!.Data);
        Assert.False(page2.Page!.HasNextPage);
        Assert.NotEqual(page1.Data[0].Id, page2.Data[0].Id);

        var badCursor = await _client.GetAsync("/api/v1/repos/default/projects?cursor=not-valid-base64!!");
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
    }

    [Fact]
    public async Task GetServices_ReturnsControllerAndService_ButNotRepository()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<List<ServiceSummaryDto>>>("/api/v1/repos/default/services");

        Assert.NotNull(response);
        Assert.Contains(response!.Data, s => s.Name == "OrderController" && s.Kind == "Controller");
        Assert.Contains(response.Data, s => s.Name == "OrderService" && s.Kind == "Service");
        Assert.DoesNotContain(response.Data, s => s.Name == "OrderRepository");
    }

    [Fact]
    public async Task GetServiceDetail_ComposesDependenciesCallersImplementsAndTests()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<ServiceDetailDto>>($"/api/v1/repos/default/services/{_service.NodeId}");

        Assert.NotNull(response);
        var detail = response!.Data;
        Assert.Equal("OrderService", detail.Name);
        Assert.Contains(detail.Dependencies, d => d.Name == "OrderRepository");
        Assert.Contains(detail.Callers, c => c.Name == "OrderController");
        Assert.Contains(detail.Implements, i => i.Name == "IOrderService");
        Assert.Contains(detail.Tests, t => t.Name == "OrderServiceTests");
    }

    [Fact]
    public async Task GetServiceDetail_UnknownId_ReturnsProblemDetails404()
    {
        var response = await _client.GetAsync("/api/v1/repos/default/services/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("node-not-found", body);
    }

    [Fact]
    public async Task GetGraph_Unscoped_ReturnsAllSeededNodesAndEdges()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<GraphResponseDto>>("/api/v1/repos/default/graph");

        Assert.NotNull(response);
        Assert.Equal(8, response!.Data.Nodes.Count);
        Assert.Equal(8, response.Data.Edges.Count);
        Assert.False(response.Data.Truncated);
    }

    [Fact]
    public async Task GetGraph_ScopedToProject_ReturnsOnlyThatProjectsNodes()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<GraphResponseDto>>($"/api/v1/repos/default/graph?scope={_projectId}");

        Assert.NotNull(response);
        Assert.Equal(7, response!.Data.Nodes.Count); // Orders' own 7 nodes only, not InfraGateway
    }

    [Fact]
    public async Task GetGraph_ScopedToNode_ExpandsNeighborhoodByDepth()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<GraphResponseDto>>($"/api/v1/repos/default/graph?scope={_service.NodeId}&depth=1");

        Assert.NotNull(response);
        Assert.Contains(response!.Data.Nodes, n => n.Name == "OrderService");
        Assert.Contains(response.Data.Nodes, n => n.Name == "OrderRepository"); // 1-hop forward
        Assert.Contains(response.Data.Nodes, n => n.Name == "OrderController"); // 1-hop reverse
    }

    [Fact]
    public async Task GetGraph_UnknownScope_ReturnsProblemDetails404()
    {
        var response = await _client.GetAsync("/api/v1/repos/default/graph?scope=does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetGraph_InvalidKind_ReturnsValidationProblem400()
    {
        var response = await _client.GetAsync("/api/v1/repos/default/graph?kinds=NotARealKind");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetImpact_ReturnsTransitiveDependentsWithDepthAndRisk()
    {
        // OrderRepository <- OrderService (depth 1) <- OrderController (depth 2)
        //                  <- InfraGateway (depth 3, via the Infrastructure -> Orders cross-project edge)
        var response = await _client.GetFromJsonAsync<ApiEnvelope<ImpactResponseDto>>($"/api/v1/repos/default/impact?nodeId={_repository.NodeId}");

        Assert.NotNull(response);
        Assert.Equal("OrderRepository", response!.Data.TargetName);
        Assert.Equal(3, response.Data.Summary.TotalAffected);

        var direct = Assert.Single(response.Data.Affected, a => a.Name == "OrderService");
        Assert.Equal(1, direct.Depth);
        Assert.Equal("Low", direct.RiskLevel);

        var transitive = Assert.Single(response.Data.Affected, a => a.Name == "OrderController");
        Assert.Equal(2, transitive.Depth);
        Assert.Equal("Medium", transitive.RiskLevel);

        var crossProject = Assert.Single(response.Data.Affected, a => a.Name == "InfraGateway");
        Assert.Equal(3, crossProject.Depth);
        Assert.Equal("High", crossProject.RiskLevel);
    }

    [Fact]
    public async Task GetImpact_UnknownNodeId_ReturnsProblemDetails404()
    {
        var response = await _client.GetAsync("/api/v1/repos/default/impact?nodeId=does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMetrics_ReturnsBasicTotals()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<MetricsResponseDto>>("/api/v1/repos/default/metrics");

        Assert.NotNull(response);
        Assert.Equal(2, response!.Data.TotalProjects);
        Assert.Equal(1, response.Data.TotalInterfaces);
        Assert.Equal(3, response.Data.TotalServices); // OrderController + OrderService + InfraGateway
    }

    [Fact]
    public async Task GetCouplingMetrics_ComputesAfferentEfferentAndInstabilityAcrossProjects()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<List<CouplingMetricDto>>>("/api/v1/repos/default/metrics/coupling");

        Assert.NotNull(response);
        var orders = Assert.Single(response!.Data, c => c.ProjectId == _projectId);
        Assert.Equal(1, orders.AfferentCoupling); // InfraGateway -> OrderController
        Assert.Equal(1, orders.EfferentCoupling); // OrderRepository -> InfraGateway
        Assert.Equal(0.5, orders.Instability);

        var infra = Assert.Single(response.Data, c => c.ProjectId == _infraProjectId);
        Assert.Equal(1, infra.AfferentCoupling);
        Assert.Equal(1, infra.EfferentCoupling);
    }

    [Fact]
    public async Task GetCircularDependencies_DetectsTwoProjectCycle()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<List<CircularDependencyDto>>>("/api/v1/repos/default/metrics/circular-dependencies");

        Assert.NotNull(response);
        var cycle = Assert.Single(response!.Data);
        Assert.Equal(2, cycle.Length);
        Assert.Contains(_projectId, cycle.Cycle);
        Assert.Contains(_infraProjectId, cycle.Cycle);
    }

    [Fact]
    public async Task PostDiagram_RendersMermaidForScopedSubgraph()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/repos/default/diagram", new DiagramRequestDto(_service.NodeId, Depth: 1));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<DiagramResponseDto>>();
        Assert.NotNull(body);
        Assert.Equal("mermaid", body!.Data.Format);
        Assert.StartsWith("graph TD", body.Data.Content);
        Assert.Contains("OrderService", body.Data.Content);
        Assert.Contains("-->|Calls|", body.Data.Content);
    }

    [Fact]
    public async Task PostDiagram_UnsupportedFormat_ReturnsValidationProblem400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/repos/default/diagram", new DiagramRequestDto(null, Format: "plantuml"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostImplementationPlan_AcceptsAndCompletesWithPlaceholderPlan()
    {
        var accepted = await _client.PostAsJsonAsync("/api/v1/repos/default/implementation-plan", new ImplementationPlanRequest("Implement Archive Order", [_projectId]));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.NotNull(accepted.Headers.Location);
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<ApiEnvelope<JobAcceptedDto>>();
        Assert.Equal("Pending", acceptedBody!.Data.Status);

        var job = await PollUntilTerminalAsync(acceptedBody.Data.JobId);
        Assert.Equal("Completed", job.Status);

        var result = ((JsonElement)job.Result!).Deserialize<ImplementationPlanResult>(_caseInsensitiveJson);
        Assert.NotNull(result);
        Assert.Contains(_projectId, result!.AffectedProjects);
        Assert.Contains("OrderService", result.ModifiedServices);
        Assert.Equal("Unknown", result.RiskLevel);
    }

    [Fact]
    public async Task PostImplementationPlan_MissingPrompt_ReturnsValidationProblem400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/repos/default/implementation-plan", new ImplementationPlanRequest(""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostArchitectureAnalysis_AcceptsAndCompletesWithRealTransitiveImpact()
    {
        var accepted = await _client.PostAsJsonAsync(
            "/api/v1/repos/default/architecture-analysis", new ArchitectureAnalysisRequest("What breaks if we remove IOrderRepository?", [_repository.NodeId]));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<ApiEnvelope<JobAcceptedDto>>();

        var job = await PollUntilTerminalAsync(acceptedBody!.Data.JobId);
        Assert.Equal("Completed", job.Status);

        var result = ((JsonElement)job.Result!).Deserialize<ArchitectureAnalysisResult>(_caseInsensitiveJson);
        Assert.NotNull(result);
        Assert.Contains("OrderRepository", result!.Summary);
        Assert.Contains(_service.NodeId, result.AffectedNodeIds); // real transitive dependent, not fabricated
    }

    [Fact]
    public async Task GetJob_UnknownId_ReturnsProblemDetails404()
    {
        var response = await _client.GetAsync("/api/v1/repos/default/jobs/job_does_not_exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSnapshots_ReturnsCurrentScanAsTheOnlyRealSnapshot()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<List<SnapshotDto>>>("/api/v1/repos/default/snapshots");

        Assert.NotNull(response);
        var snapshot = Assert.Single(response!.Data);
        Assert.Equal(2, snapshot.ProjectCount);
        Assert.StartsWith("snap_", snapshot.SnapshotId);

        var byId = await _client.GetFromJsonAsync<ApiEnvelope<SnapshotDto>>($"/api/v1/repos/default/snapshots/{snapshot.SnapshotId}");
        Assert.Equal(snapshot.SnapshotId, byId!.Data.SnapshotId);

        var unknown = await _client.GetAsync("/api/v1/repos/default/snapshots/snap_does_not_exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task GetSnapshotDiff_ReturnsNotImplemented_NoHistoricalDataExistsYet()
    {
        var response = await _client.GetAsync("/api/v1/repos/default/snapshots/snap_1/diff?against=snap_0");
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task GetQualityScore_ComputesWeightedFactors()
    {
        var response = await _client.GetFromJsonAsync<ApiEnvelope<QualityScoreDto>>("/api/v1/repos/default/quality-score");

        Assert.NotNull(response);
        Assert.Equal(3, response!.Data.Factors.Count);
        Assert.InRange(response.Data.OverallScore, 0, 100);
        Assert.Contains(response.Data.Factors, f => f.Name == "Coupling");
        Assert.Contains(response.Data.Factors, f => f.Name == "CircularDependencies");
        Assert.Contains(response.Data.Factors, f => f.Name == "TestCoverageProxy");
    }

    [Fact]
    public async Task InvitationFlow_CreateThenAccept_GrantsRepoMembership()
    {
        var created = await _client.PostAsJsonAsync("/api/v1/repos/default/invitations", new CreateInvitationRequest("teammate@example.com", "Maintainer"));
        created.EnsureSuccessStatusCode();
        var invitation = (await created.Content.ReadFromJsonAsync<ApiEnvelope<InvitationDto>>())!.Data;
        Assert.Equal("Pending", invitation.Status);

        var accepted = await _client.PostAsync($"/api/v1/repos/default/invitations/{invitation.InvitationId}/accept", content: null);
        accepted.EnsureSuccessStatusCode();
        var membership = (await accepted.Content.ReadFromJsonAsync<ApiEnvelope<MembershipDto>>())!.Data;
        Assert.Equal("default", membership.RepoId);
        Assert.Equal("Maintainer", membership.Role);

        var unknownAccept = await _client.PostAsync("/api/v1/repos/default/invitations/inv_does_not_exist/accept", content: null);
        Assert.Equal(HttpStatusCode.NotFound, unknownAccept.StatusCode);
    }

    [Fact]
    public async Task CreateInvitation_UnknownRole_ReturnsValidationProblem400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/repos/default/invitations", new CreateInvitationRequest("teammate@example.com", "SuperAdmin"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ArchitectureHub_GraphUpdated_IsDeliveredToConnectedClient()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/architecture"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        var received = new TaskCompletionSource<GraphUpdatedEvent>();
        connection.On<GraphUpdatedEvent>("graph:updated", evt => received.TrySetResult(evt));
        await connection.StartAsync();

        // Simulates the Incremental Watcher's trigger point (05-rest-api.md Section 9.1) — no real
        // watcher process exists yet, so this drives the same seam it would use.
        var notifier = _factory.Services.GetRequiredService<IArchitectureChangeNotifier>();
        var fakeEvent = new GraphUpdatedEvent(
            "chg_test", DateTimeOffset.UtcNow, ["n_added"], [], [], [_projectId], new GraphChangeSummary(1, 0, 0));
        await notifier.GraphUpdatedAsync("default", fakeEvent);

        var deliveredEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("chg_test", deliveredEvent.ChangeId);
        Assert.Equal(["n_added"], deliveredEvent.AddedNodeIds);
    }

    private async Task<JobStatusResponseDto> PollUntilTerminalAsync(string jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetFromJsonAsync<ApiEnvelope<JobStatusResponseDto>>($"/api/v1/repos/default/jobs/{jobId}");
            if (response!.Data.Status is "Completed" or "Failed")
            {
                return response.Data;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Job {jobId} did not reach a terminal state in time.");
    }

    private static NodeDto MakeNode(IdGenerator ids, string projectId, string name, NodeType type) => new()
    {
        NodeId = ids.NodeId(projectId, null, $"Orders.{name}", type),
        ProjectId = projectId,
        NodeType = type,
        Name = name,
        FullName = $"Orders.{name}",
    };

    private static EdgeDto Edge(IdGenerator ids, NodeDto source, NodeDto target, RelationshipType type) => new()
    {
        EdgeId = ids.EdgeId(source.NodeId, target.NodeId, type),
        SourceId = source.NodeId,
        TargetId = target.NodeId,
        RelationshipType = type,
    };
}

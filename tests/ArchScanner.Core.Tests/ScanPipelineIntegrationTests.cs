using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Configuration;
using ArchScanner.Core.Output;

namespace ArchScanner.Core.Tests;

/// <summary>
/// Integration test (Section 8.2): runs the full pipeline — real MSBuildWorkspace, Pass 1, Pass 2,
/// heuristics, NdjsonGraphWriter — against the checked-in SampleErpSolution fixture. Slower than
/// the unit tests above; this is the one that proves the whole chain actually works together,
/// not just each piece in isolation.
/// </summary>
public class ScanPipelineIntegrationTests
{
    private static string FindSampleSolutionPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArchIntel.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repo root (ArchIntel.slnx) from test base directory.");
        }

        var solutionPath = Path.Combine(dir.FullName, "samples", "SampleErpSolution", "SampleErpSolution.sln");
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException("Sample ERP solution fixture not found.", solutionPath);
        }

        return solutionPath;
    }

    private static async Task<(IReadOnlyList<ArchIntel.GraphStore.Contracts.NodeDto> Nodes, IReadOnlyList<ArchIntel.GraphStore.Contracts.EdgeDto> Edges)> RunScanAsync(string outputDir)
    {
        var config = new ScanConfig
        {
            Solution = FindSampleSolutionPath(),
            ScanOrder = ["Common", "Domain", "Application", "Infrastructure", "Api", "Tests"],
            Ignore = ["bin", "obj"],
        };

        var writer = new NdjsonGraphWriter(outputDir);
        var pipeline = new ScanPipeline(writer, new IdGenerator());
        var result = await pipeline.RunAsync(config);

        Assert.Empty(result.Warnings);
        Assert.True(result.Summary.NodeCount > 0);
        Assert.True(result.Summary.EdgeCount > 0);

        var nodesJson = await File.ReadAllLinesAsync(Path.Combine(outputDir, "nodes.ndjson"));
        var edgesJson = await File.ReadAllLinesAsync(Path.Combine(outputDir, "edges.ndjson"));

        var nodes = nodesJson.Select(l => System.Text.Json.JsonSerializer.Deserialize<ArchIntel.GraphStore.Contracts.NodeDto>(l)!).ToList();
        var edges = edgesJson.Select(l => System.Text.Json.JsonSerializer.Deserialize<ArchIntel.GraphStore.Contracts.EdgeDto>(l)!).ToList();

        return (nodes, edges);
    }

    [Fact]
    public async Task FullScan_OfSampleErpSolution_DiscoversAllSixProjects()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"archintel-scan-{Guid.NewGuid():N}");
        try
        {
            var (nodes, _) = await RunScanAsync(outputDir);

            var projectNames = nodes.Where(n => n.NodeType == NodeType.Project).Select(n => n.Name).ToList();
            Assert.Contains("SampleErp.Common", projectNames);
            Assert.Contains("SampleErp.Domain", projectNames);
            Assert.Contains("SampleErp.Application", projectNames);
            Assert.Contains("SampleErp.Infrastructure", projectNames);
            Assert.Contains("SampleErp.Api", projectNames);
            Assert.Contains("SampleErp.Tests", projectNames);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullScan_ClassifiesEveryHeuristicNodeType_AtLeastOnce()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"archintel-scan-{Guid.NewGuid():N}");
        try
        {
            var (nodes, edges) = await RunScanAsync(outputDir);

            Assert.Contains(nodes, n => n.NodeType == NodeType.Controller && n.Name == "OrdersController");
            Assert.Contains(nodes, n => n.NodeType == NodeType.MediatRHandler && n.Name == "CreateOrderCommandHandler");
            Assert.Contains(nodes, n => n.NodeType == NodeType.DomainEvent && n.Name == "OrderCreatedEvent");
            Assert.Contains(nodes, n => n.NodeType == NodeType.EfDbContext && n.Name == "AppDbContext");
            Assert.Contains(nodes, n => n.NodeType == NodeType.EfEntity && n.Name == "Order");
            Assert.Contains(nodes, n => n.NodeType == NodeType.Repository && n.Name == "OrderRepository");
            Assert.Contains(nodes, n => n.NodeType == NodeType.BackgroundWorker && n.Name == "OrderSyncWorker");
            Assert.Contains(nodes, n => n.NodeType == NodeType.ConfigurationSection && n.Name == "SmtpSettings");
            Assert.Contains(nodes, n => n.NodeType == NodeType.TestClass && n.Name == "CreateOrderCommandHandlerTests");
            Assert.Contains(nodes, n => n.NodeType == NodeType.TestMethod && n.Name == "Handle_SavesOrder_WithRequestedTotal");
            Assert.Contains(nodes, n => n.NodeType == NodeType.MinimalApiEndpoint);

            // The interface -> concrete DI mapping (Owns) proves ProjectSignalsScanner + DiOwnsEdgeResolver
            // ran correctly end to end, not just in isolation.
            var repository = nodes.Single(n => n.NodeType == NodeType.Repository && n.Name == "OrderRepository");
            var repositoryInterface = nodes.Single(n => n.NodeType == NodeType.Interface && n.Name == "IOrderRepository");
            Assert.Contains(edges, e => e.SourceId == repositoryInterface.NodeId && e.TargetId == repository.NodeId && e.RelationshipType == RelationshipType.Owns);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullScan_ProducesProjectReferenceEdges_MatchingActualProjectReferences()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"archintel-scan-{Guid.NewGuid():N}");
        try
        {
            var (nodes, edges) = await RunScanAsync(outputDir);

            var domain = nodes.Single(n => n.NodeType == NodeType.Project && n.Name == "SampleErp.Domain");
            var common = nodes.Single(n => n.NodeType == NodeType.Project && n.Name == "SampleErp.Common");

            Assert.Contains(edges, e => e.SourceId == domain.NodeId && e.TargetId == common.NodeId && e.RelationshipType == RelationshipType.References);
        }
        finally
        {
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullScan_IsDeterministic_AcrossTwoRuns()
    {
        // Section 8.4: run the full pipeline twice against the same solution and assert
        // byte-identical (post-normalization) output — proves node/edge ids don't depend on
        // scan-to-scan incidentals like dictionary/task ordering.
        var outputDir1 = Path.Combine(Path.GetTempPath(), $"archintel-scan-{Guid.NewGuid():N}");
        var outputDir2 = Path.Combine(Path.GetTempPath(), $"archintel-scan-{Guid.NewGuid():N}");
        try
        {
            var (nodes1, edges1) = await RunScanAsync(outputDir1);
            var (nodes2, edges2) = await RunScanAsync(outputDir2);

            var ids1 = nodes1.Select(n => n.NodeId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var ids2 = nodes2.Select(n => n.NodeId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            Assert.Equal(ids1, ids2);

            var edgeIds1 = edges1.Select(e => e.EdgeId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var edgeIds2 = edges2.Select(e => e.EdgeId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            Assert.Equal(edgeIds1, edgeIds2);
        }
        finally
        {
            if (Directory.Exists(outputDir1)) Directory.Delete(outputDir1, recursive: true);
            if (Directory.Exists(outputDir2)) Directory.Delete(outputDir2, recursive: true);
        }
    }
}

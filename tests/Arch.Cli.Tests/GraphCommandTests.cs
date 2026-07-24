using Arch.Cli.Commands;
using Arch.Cli.Tests.Fixtures;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;

namespace Arch.Cli.Tests;

public sealed class GraphCommandTests : IAsyncLifetime
{
    private readonly SqliteFixture _fixture = new();
    private readonly IdGenerator _ids = new();
    private DirectoryInfo _tempDir = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _tempDir = Directory.CreateTempSubdirectory("archcli-graph-");

        var projectId = _ids.ProjectId("Test.sln", "SampleErp.Business.csproj");
        var project = new ProjectDto { ProjectId = projectId, Name = "SampleErp.Business", Path = "Business.csproj" };

        // A second project sharing the "Business" substring, so ambiguous --project lookups are testable.
        var otherProjectId = _ids.ProjectId("Test.sln", "SampleErp.BusinessReporting.csproj");
        var otherProject = new ProjectDto { ProjectId = otherProjectId, Name = "SampleErp.BusinessReporting", Path = "BusinessReporting.csproj" };

        var controller = MakeNode(projectId, "Business.OrderController", NodeType.Controller);
        var service = MakeNode(projectId, "Business.OrderService", NodeType.Service);
        var repository = MakeNode(projectId, "Business.OrderRepository", NodeType.Repository);

        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, project);
        await _fixture.Writer.UpsertProjectAsync(scan, otherProject);
        await _fixture.Writer.UpsertNodesAsync(scan, [controller, service, repository]);
        await _fixture.Writer.UpsertEdgesAsync(scan,
        [
            MakeEdge(controller.NodeId, service.NodeId, RelationshipType.Calls),
            MakeEdge(service.NodeId, repository.NodeId, RelationshipType.Calls),
        ]);
        await _fixture.Writer.CompleteScanAsync(scan);

        await File.WriteAllTextAsync(Path.Combine(_tempDir.FullName, "arch.yml"), $"""
            solution: dummy.sln
            storage:
              connectionString: {_fixture.DbPath}
            """);
    }

    public async Task DisposeAsync()
    {
        _tempDir.Delete(recursive: true);
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_NoArgs_ListsProjectsAsTable()
    {
        var output = new CapturingOutputWriter();

        var exitCode = await GraphCommand.RunAsync(null, _tempDir.FullName, null, null, null, 1, false, output);

        Assert.Equal(ExitCodes.Success, exitCode);
        var table = Assert.Single(output.Tables);
        Assert.Contains(table.Rows, row => row[0] == "SampleErp.Business");
    }

    [Fact]
    public async Task RunAsync_ProjectScoped_ExactName_RendersTreeWithDependencies()
    {
        var output = new CapturingOutputWriter();

        var exitCode = await GraphCommand.RunAsync(null, _tempDir.FullName, null, "SampleErp.Business", null, 1, false, output);

        Assert.Equal(ExitCodes.Success, exitCode);
        var tree = Assert.Single(output.Trees);
        Assert.Equal("SampleErp.Business (project)", tree.Label);
        Assert.Contains(tree.Children, c => c.Label.Contains("OrderController") && c.Children.Any(g => g.Label.Contains("OrderService")));
    }

    [Fact]
    public async Task RunAsync_ProjectScoped_UnambiguousSubstring_FallsBackAndResolves()
    {
        var output = new CapturingOutputWriter();

        // "Reporting" isn't an exact project name, but uniquely substring-matches SampleErp.BusinessReporting.
        var exitCode = await GraphCommand.RunAsync(null, _tempDir.FullName, null, "Reporting", null, 1, false, output);

        Assert.Equal(ExitCodes.Success, exitCode);
        var tree = Assert.Single(output.Trees);
        Assert.Equal("SampleErp.BusinessReporting (project)", tree.Label);
    }

    [Fact]
    public async Task RunAsync_ProjectScoped_AmbiguousSubstring_ReturnsUserError()
    {
        var output = new CapturingOutputWriter();

        // "Business" matches both "SampleErp.Business" and "SampleErp.BusinessReporting".
        var exitCode = await GraphCommand.RunAsync(null, _tempDir.FullName, null, "Business", null, 1, false, output);

        Assert.Equal(ExitCodes.UserError, exitCode);
        Assert.Contains(output.Errors, e => e.Message.Contains("Multiple projects match"));
    }

    [Fact]
    public async Task RunAsync_NodeScoped_ShowsDependenciesAndCallers()
    {
        var output = new CapturingOutputWriter();

        var exitCode = await GraphCommand.RunAsync(null, _tempDir.FullName, "OrderService", null, null, 1, false, output);

        Assert.Equal(ExitCodes.Success, exitCode);
        var tree = Assert.Single(output.Trees);
        Assert.Contains("OrderService", tree.Label);
        var dependsOn = Assert.Single(tree.Children, c => c.Label == "Depends on");
        Assert.Contains(dependsOn.Children, c => c.Label.Contains("OrderRepository"));
        var usedBy = Assert.Single(tree.Children, c => c.Label == "Used by");
        Assert.Contains(usedBy.Children, c => c.Label.Contains("OrderController"));
    }

    [Fact]
    public async Task RunAsync_UnknownProject_ReturnsUserError()
    {
        var output = new CapturingOutputWriter();

        var exitCode = await GraphCommand.RunAsync(null, _tempDir.FullName, null, "NoSuchProject", null, 1, false, output);

        Assert.Equal(ExitCodes.UserError, exitCode);
        Assert.Single(output.Errors);
    }

    private NodeDto MakeNode(string projectId, string fullName, NodeType type) => new()
    {
        NodeId = _ids.NodeId(projectId, null, fullName, type),
        ProjectId = projectId,
        NodeType = type,
        Name = fullName.Split('.')[^1],
        FullName = fullName,
    };

    private EdgeDto MakeEdge(string sourceId, string targetId, RelationshipType type) => new()
    {
        EdgeId = _ids.EdgeId(sourceId, targetId, type),
        SourceId = sourceId,
        TargetId = targetId,
        RelationshipType = type,
    };
}

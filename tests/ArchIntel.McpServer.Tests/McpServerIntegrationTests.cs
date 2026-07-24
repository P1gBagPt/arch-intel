using System.Text.Json;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchIntel.GraphStore.Sqlite;
using ArchIntel.McpServer.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ArchIntel.McpServer.Tests;

/// <summary>
/// The highest-value test from 04-mcp-server.md Section 8.4: launches the real, built `arch`
/// executable as a stdio child process (exactly how an AI IDE would) against a seeded SQLite
/// database, and drives it with the actual MCP client SDK — catches DI wiring/serialization bugs
/// the in-process ToolTests can't.
/// </summary>
public sealed class McpServerIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"archmcp-it-{Guid.NewGuid():N}.db");
    private DirectoryInfo _tempDir = null!;

    public async Task InitializeAsync()
    {
        _tempDir = Directory.CreateTempSubdirectory("archmcp-it-");

        var connectionFactory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        await new MigrationRunner(connectionFactory).ApplyAsync();
        var writer = new SqliteGraphWriter(connectionFactory);
        var ids = new IdGenerator();

        var projectId = ids.ProjectId("Test.sln", "Orders.csproj");
        var project = new ProjectDto { ProjectId = projectId, Name = "Orders", Path = "Orders.csproj" };
        var controller = MakeNode(ids, projectId, "OrderController", NodeType.Controller);
        var service = MakeNode(ids, projectId, "OrderService", NodeType.Service);
        var repository = MakeNode(ids, projectId, "OrderRepository", NodeType.Repository);

        var scan = await writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await writer.UpsertProjectAsync(scan, project);
        await writer.UpsertNodesAsync(scan, [controller, service, repository]);
        await writer.UpsertEdgesAsync(scan,
        [
            new EdgeDto { EdgeId = ids.EdgeId(controller.NodeId, service.NodeId, RelationshipType.Calls), SourceId = controller.NodeId, TargetId = service.NodeId, RelationshipType = RelationshipType.Calls },
            new EdgeDto { EdgeId = ids.EdgeId(service.NodeId, repository.NodeId, RelationshipType.Calls), SourceId = service.NodeId, TargetId = repository.NodeId, RelationshipType = RelationshipType.Calls },
        ]);
        await writer.CompleteScanAsync(scan);

        await File.WriteAllTextAsync(Path.Combine(_tempDir.FullName, "arch.yml"), $"""
            solution: dummy.sln
            storage:
              connectionString: {_dbPath}
            """);
    }

    public Task DisposeAsync()
    {
        _tempDir.Delete(recursive: true);

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task RealStdioServer_ListsToolsAndAnswersFindDependencies()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // typeof(Arch.Cli.ExitCodes) is just a stable, public anchor for locating the built arch
        // assembly — the actual child process is launched via `dotnet <dll> mcp start`, which works
        // regardless of platform-specific apphost naming.
        var archDllPath = typeof(Arch.Cli.ExitCodes).Assembly.Location;
        var configPath = Path.Combine(_tempDir.FullName, "arch.yml");

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "arch-mcp-integration-test",
                Command = "dotnet",
                Arguments = [archDllPath, "mcp", "start", "--config", configPath],
            },
            NullLoggerFactory.Instance);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        Assert.Contains(tools, t => t.Name == "find_dependencies");
        Assert.Contains(tools, t => t.Name == "find_callers");
        Assert.Contains(tools, t => t.Name == "find_service");

        var callResult = await client.CallToolAsync(
            "find_dependencies",
            new Dictionary<string, object?> { ["symbolName"] = "OrderService" },
            cancellationToken: cts.Token);

        Assert.True(callResult.IsError is null or false, "expected the tool call to succeed");
        var result = DeserializeResult<FindDependenciesResult>(callResult);

        Assert.NotNull(result.RootNode);
        Assert.Equal("OrderService", result.RootNode!.Name);
        var dependency = Assert.Single(result.Dependencies);
        Assert.Equal("OrderRepository", dependency.Node.Name);
        Assert.Equal("Calls", dependency.Relationship);
    }

    private static T DeserializeResult<T>(CallToolResult callResult)
    {
        if (callResult.StructuredContent is { } structured)
        {
            return structured.Deserialize<T>(JsonOptions)!;
        }

        var text = callResult.Content.OfType<TextContentBlock>().First().Text;
        return JsonSerializer.Deserialize<T>(text, JsonOptions)!;
    }

    private static NodeDto MakeNode(IdGenerator ids, string projectId, string name, NodeType type) => new()
    {
        NodeId = ids.NodeId(projectId, null, $"Orders.{name}", type),
        ProjectId = projectId,
        NodeType = type,
        Name = name,
        FullName = $"Orders.{name}",
    };
}

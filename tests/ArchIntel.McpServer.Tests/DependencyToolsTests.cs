using ArchIntel.McpServer.Tests.Fixtures;
using ArchIntel.McpServer.Tools;

namespace ArchIntel.McpServer.Tests;

public sealed class DependencyToolsTests
{
    private readonly FixtureGraphReader _reader = new();
    private readonly DependencyTools _tools;

    public DependencyToolsTests()
    {
        _tools = new DependencyTools(_reader);
    }

    [Fact]
    public async Task FindDependencies_ReturnsDirectDependencies_WithGraphVersionStamp()
    {
        var result = await _tools.FindDependencies("OrderService");

        Assert.NotNull(result.RootNode);
        Assert.Equal("OrderService", result.RootNode!.Name);
        Assert.Equal(3, result.Dependencies.Count);
        Assert.Contains(result.Dependencies, d => d.Relationship == "Implements" && d.Node.Name == "IOrderService" && d.Depth == 1);
        Assert.Contains(result.Dependencies, d => d.Relationship == "Injects" && d.Node.Name == "IOrderRepository" && d.Depth == 1);
        Assert.Contains(result.Dependencies, d => d.Relationship == "Calls" && d.Node.Name == "OrderNotifier" && d.Depth == 1);
        Assert.False(result.Truncated);
        Assert.Equal("2026-07-24T02:11:00Z#4821", result.GraphVersion);
        Assert.NotNull(result.LastScannedAt);
    }

    [Fact]
    public async Task FindDependencies_FiltersByRelationshipKind()
    {
        var result = await _tools.FindDependencies("OrderService", relationshipKinds: ["Implements"]);

        var dependency = Assert.Single(result.Dependencies);
        Assert.Equal("IOrderService", dependency.Node.Name);
    }

    [Fact]
    public async Task FindDependencies_UnknownSymbol_ReturnsEmptyResult_NotAnException()
    {
        var result = await _tools.FindDependencies("NoSuchSymbol");

        Assert.Null(result.RootNode);
        Assert.Empty(result.Dependencies);
        Assert.Contains("No node found", result.Message);
    }

    [Fact]
    public async Task FindDependencies_DepthOutOfRange_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _tools.FindDependencies("OrderService", depth: 6));
    }

    [Fact]
    public async Task FindDependencies_UnknownRelationshipKind_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tools.FindDependencies("OrderService", relationshipKinds: ["NotARealKind"]));
    }

    [Fact]
    public async Task FindCallers_ReturnsInboundEdges()
    {
        var result = await _tools.FindCallers("IOrderService");

        Assert.NotNull(result.RootNode);
        Assert.Equal(2, result.Callers.Count);
        Assert.Contains(result.Callers, c => c.Relationship == "Calls" && c.Node.Name == "OrderController");
        Assert.Contains(result.Callers, c => c.Relationship == "Implements" && c.Node.Name == "OrderService");
    }

    [Fact]
    public async Task FindCallers_UnknownSymbol_ReturnsEmptyResult_NotAnException()
    {
        var result = await _tools.FindCallers("NoSuchSymbol");

        Assert.Null(result.RootNode);
        Assert.Empty(result.Callers);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task FindDependencies_NoCompletedScan_OmitsGraphVersion()
    {
        _reader.ClearScanMetadata();

        var result = await _tools.FindDependencies("OrderService");

        Assert.Null(result.GraphVersion);
        Assert.Null(result.LastScannedAt);
    }

    [Fact]
    public async Task FindDependencies_Depth2_WalksASecondHop_WithCorrectDepthPerNode()
    {
        // OrderService -> OrderNotifier (depth 1) -> IEmailSender (depth 2); the README chain's
        // other two depth-1 edges (IOrderService, IOrderRepository) are dead ends, so this is the
        // one path in the fixture that actually exercises real multi-level BFS nesting.
        var result = await _tools.FindDependencies("OrderService", depth: 2);

        Assert.Equal(4, result.Dependencies.Count);
        Assert.Contains(result.Dependencies, d => d.Node.Name == "IEmailSender" && d.Relationship == "Calls" && d.Depth == 2);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FindDependencies_WithCycle_TerminatesAndExcludesTheRootItself()
    {
        // OrderService -> OrderNotifier -> IEmailSender -> OrderService: a cycle back to the root.
        _reader.AddCustomEdge(_reader.IEmailSender, _reader.OrderService, ArchIntel.GraphStore.Contracts.Enums.RelationshipType.Calls);

        var result = await _tools.FindDependencies("OrderService", depth: 5);

        Assert.DoesNotContain(result.Dependencies, d => d.Node.Name == "OrderService");
        Assert.Equal(4, result.Dependencies.Count);
    }

    [Fact]
    public async Task FindCallers_Depth2_WalksBackwardMultipleHops()
    {
        var result = await _tools.FindCallers("IEmailSender", depth: 2);

        Assert.Equal(2, result.Callers.Count);
        Assert.Contains(result.Callers, c => c.Node.Name == "OrderNotifier" && c.Depth == 1);
        Assert.Contains(result.Callers, c => c.Node.Name == "OrderService" && c.Depth == 2);
    }
}

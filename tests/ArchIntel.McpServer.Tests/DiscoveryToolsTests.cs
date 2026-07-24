using ArchIntel.McpServer.Tests.Fixtures;
using ArchIntel.McpServer.Tools;

namespace ArchIntel.McpServer.Tests;

public sealed class DiscoveryToolsTests
{
    private readonly FixtureGraphReader _reader = new();
    private readonly DiscoveryTools _tools;

    public DiscoveryToolsTests()
    {
        _tools = new DiscoveryTools(_reader);
    }

    [Fact]
    public async Task FindService_DefaultKinds_MatchesServiceAndController_ButNotRepository()
    {
        var result = await _tools.FindService("Order");

        Assert.Contains(result.Matches, m => m.Name == "OrderService");
        Assert.Contains(result.Matches, m => m.Name == "OrderController");
        Assert.DoesNotContain(result.Matches, m => m.Name == "OrderRepository");
    }

    [Fact]
    public async Task FindService_ExplicitKinds_NarrowsToThatKind()
    {
        var result = await _tools.FindService("Order", kinds: ["Repository"]);

        var match = Assert.Single(result.Matches);
        Assert.Equal("OrderRepository", match.Name);
    }

    [Fact]
    public async Task FindService_MaxResults_TruncatesAndReportsIt()
    {
        var result = await _tools.FindService("Order", maxResults: 1);

        Assert.Single(result.Matches);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task FindService_NoMatch_ReturnsEmptyNotAnException()
    {
        var result = await _tools.FindService("NoSuchThing");

        Assert.Empty(result.Matches);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FindService_UnknownKind_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tools.FindService("Order", kinds: ["NotAKind"]));
    }

    [Fact]
    public async Task FindService_MaxResultsOutOfRange_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _tools.FindService("Order", maxResults: 0));
    }
}

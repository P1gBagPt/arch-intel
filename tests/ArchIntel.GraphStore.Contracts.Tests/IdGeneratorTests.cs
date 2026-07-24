using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;

namespace ArchIntel.GraphStore.Contracts.Tests;

public class IdGeneratorTests
{
    private readonly IdGenerator _sut = new();

    [Fact]
    public void NodeId_IsDeterministic_ForSameInputs()
    {
        var a = _sut.NodeId("proj1", "PatternVision.Orders", "PatternVision.Orders.OrderService", NodeType.Class);
        var b = _sut.NodeId("proj1", "PatternVision.Orders", "PatternVision.Orders.OrderService", NodeType.Class);

        Assert.Equal(a, b);
    }

    [Fact]
    public void NodeId_Differs_ForDifferentNodeType()
    {
        var classId = _sut.NodeId("proj1", "NS", "NS.Foo", NodeType.Class);
        var interfaceId = _sut.NodeId("proj1", "NS", "NS.Foo", NodeType.Interface);

        Assert.NotEqual(classId, interfaceId);
    }

    [Fact]
    public void NodeId_Differs_ForOverloadedMethods()
    {
        // Global symbol key is expected to already encode parameter types for overload
        // disambiguation (Section 3.3) — verify the generator doesn't collapse two distinct
        // fully-qualified signatures into the same id.
        var overload1 = _sut.NodeId("proj1", "NS", "NS.Foo.Bar(System.String)", NodeType.Method);
        var overload2 = _sut.NodeId("proj1", "NS", "NS.Foo.Bar(System.Int32)", NodeType.Method);

        Assert.NotEqual(overload1, overload2);
    }

    [Fact]
    public void NodeId_Differs_ForNestedClasses_WithSameSimpleName()
    {
        var outer = _sut.NodeId("proj1", "NS", "NS.Outer.Inner", NodeType.Class);
        var topLevel = _sut.NodeId("proj1", "NS", "NS.Inner", NodeType.Class);

        Assert.NotEqual(outer, topLevel);
    }

    [Fact]
    public void EdgeId_IsDeterministic_AndDirectional()
    {
        var forward = _sut.EdgeId("a", "b", RelationshipType.Calls);
        var forwardAgain = _sut.EdgeId("a", "b", RelationshipType.Calls);
        var reverse = _sut.EdgeId("b", "a", RelationshipType.Calls);

        Assert.Equal(forward, forwardAgain);
        Assert.NotEqual(forward, reverse);
    }

    [Fact]
    public void ProjectId_IsDeterministic()
    {
        var a = _sut.ProjectId("MySolution.sln", "src/Orders/Orders.csproj");
        var b = _sut.ProjectId("MySolution.sln", "src/Orders/Orders.csproj");

        Assert.Equal(a, b);
    }
}

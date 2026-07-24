using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Discovery;

namespace ArchScanner.Core.Tests;

public class ArchDeclarationWalkerTests
{
    // Deliberately named to avoid matching any TypeClassifier heuristic (Repository/Service/etc.)
    // — this fixture tests generic Pass 1 structural behavior, not classification semantics
    // (those are covered separately in TypeClassifierTests).
    private const string SampleCode = """
        namespace Sample.Orders
        {
            public interface IOrderThing
            {
            }

            public class OrderThing : IOrderThing
            {
                private readonly int _count;

                public OrderThing()
                {
                }

                public int Count => _count;

                public void Save()
                {
                }
            }
        }
        """;

    // ArchDeclarationWalker treats projectNodeId as an opaque container id supplied by its caller
    // (DiscoveryPass owns emitting the actual Project NodeDto) — expose it so tests can assert
    // against the top of the Contains hierarchy without depending on DiscoveryPass.
    private static (IReadOnlyList<NodeDto> Nodes, IReadOnlyList<EdgeDto> Edges, string ProjectNodeId) Walk(string code)
    {
        var (compilation, tree) = TestCompilationFactory.CreateSingleFile(code);
        var semanticModel = compilation.GetSemanticModel(tree);

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var edges = new ConcurrentBag<EdgeDto>();

        const string projectId = "proj1";
        var projectNodeId = nodeIdFactory.ForProject(projectId);

        var walker = new ArchDeclarationWalker(
            semanticModel, projectId, "Test.cs", projectNodeId,
            nodeIdFactory, registry, idGenerator, nodes, edges);
        walker.Visit(tree.GetRoot());

        return (nodes.ToList(), edges.ToList(), projectNodeId);
    }

    [Fact]
    public void Walk_EmitsANodeForEveryDeclaredSymbolKind()
    {
        var (nodes, _, _) = Walk(SampleCode);

        Assert.Contains(nodes, n => n.NodeType == NodeType.Namespace && n.FullName == "Sample.Orders");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Interface && n.Name == "IOrderThing");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Class && n.Name == "OrderThing");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Constructor);
        Assert.Contains(nodes, n => n.NodeType == NodeType.Property && n.Name == "Count");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Method && n.Name == "Save");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Field && n.Name == "_count");
    }

    [Fact]
    public void Walk_EmitsContainsHierarchy_ProjectToNamespaceToTypeToMember()
    {
        var (nodes, edges, projectNodeId) = Walk(SampleCode);

        var namespaceNode = nodes.Single(n => n.NodeType == NodeType.Namespace);
        var classNode = nodes.Single(n => n.NodeType == NodeType.Class);
        var methodNode = nodes.Single(n => n.NodeType == NodeType.Method);

        Assert.Contains(edges, e => e.SourceId == projectNodeId && e.TargetId == namespaceNode.NodeId && e.RelationshipType == RelationshipType.Contains);
        Assert.Contains(edges, e => e.SourceId == namespaceNode.NodeId && e.TargetId == classNode.NodeId && e.RelationshipType == RelationshipType.Contains);
        Assert.Contains(edges, e => e.SourceId == classNode.NodeId && e.TargetId == methodNode.NodeId && e.RelationshipType == RelationshipType.Contains);
    }

    [Fact]
    public void Walk_DeduplicatesPartialClassDeclarations_ButStillVisitsMembersFromBothParts()
    {
        const string code = """
            namespace Sample
            {
                public partial class Widget
                {
                    public void MethodA() { }
                }

                public partial class Widget
                {
                    public void MethodB() { }
                }
            }
            """;

        var (nodes, _, _) = Walk(code);

        Assert.Single(nodes, n => n.NodeType == NodeType.Class);
        Assert.Contains(nodes, n => n.NodeType == NodeType.Method && n.Name == "MethodA");
        Assert.Contains(nodes, n => n.NodeType == NodeType.Method && n.Name == "MethodB");
    }

    [Fact]
    public void Walk_DistinguishesNestedTypes_WithSameSimpleNameAsATopLevelType()
    {
        const string code = """
            namespace Sample
            {
                public class Outer
                {
                    public class Inner { }
                }

                public class Inner { }
            }
            """;

        var (nodes, _, _) = Walk(code);

        var classNodes = nodes.Where(n => n.NodeType == NodeType.Class && n.Name == "Inner").ToList();
        Assert.Equal(2, classNodes.Count);
        Assert.NotEqual(classNodes[0].NodeId, classNodes[1].NodeId);
    }
}

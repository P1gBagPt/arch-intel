using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Resolution;

namespace ArchScanner.Core.Tests;

public class RelationshipWalkerTests
{
    private const string SampleCode = """
        namespace Sample
        {
            public interface IGreeter
            {
                void Greet();
            }

            public class BaseGreeter
            {
                public virtual void Log() { }
            }

            public class Greeter : BaseGreeter, IGreeter
            {
                public void Greet()
                {
                    Log();
                    Helper();
                }

                private void Helper() { }
            }
        }
        """;

    private static (IReadOnlyList<NodeDto> Nodes, IReadOnlyList<EdgeDto> Edges) DiscoverThenResolve(string code)
    {
        var (compilation, tree) = TestCompilationFactory.CreateSingleFile(code);
        var semanticModel = compilation.GetSemanticModel(tree);

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var discoveryEdges = new ConcurrentBag<EdgeDto>();

        const string projectId = "proj1";
        var projectNodeId = nodeIdFactory.ForProject(projectId);

        // Pass 1
        new ArchDeclarationWalker(semanticModel, projectId, "Test.cs", projectNodeId, nodeIdFactory, registry, idGenerator, nodes, discoveryEdges)
            .Visit(tree.GetRoot());

        // Pass 2 — re-walks the same tree once the registry is fully populated.
        var relationshipEdges = new ConcurrentBag<EdgeDto>();
        new RelationshipWalker(semanticModel, registry, idGenerator, relationshipEdges).Visit(tree.GetRoot());

        return (nodes.ToList(), relationshipEdges.ToList());
    }

    [Fact]
    public void Resolve_EmitsImplementsEdge_FromClassToInterface()
    {
        var (nodes, edges) = DiscoverThenResolve(SampleCode);

        var greeterClass = nodes.Single(n => n.NodeType == NodeType.Class && n.Name == "Greeter");
        var greeterInterface = nodes.Single(n => n.NodeType == NodeType.Interface && n.Name == "IGreeter");

        Assert.Contains(edges, e => e.SourceId == greeterClass.NodeId && e.TargetId == greeterInterface.NodeId && e.RelationshipType == RelationshipType.Implements);
    }

    [Fact]
    public void Resolve_EmitsInheritsEdge_FromClassToBaseClass_ButNotToSystemObject()
    {
        var (nodes, edges) = DiscoverThenResolve(SampleCode);

        var greeterClass = nodes.Single(n => n.NodeType == NodeType.Class && n.Name == "Greeter");
        var baseClass = nodes.Single(n => n.NodeType == NodeType.Class && n.Name == "BaseGreeter");

        Assert.Contains(edges, e => e.SourceId == greeterClass.NodeId && e.TargetId == baseClass.NodeId && e.RelationshipType == RelationshipType.Inherits);
        Assert.DoesNotContain(edges, e => e.RelationshipType == RelationshipType.Inherits && e.TargetId.Contains("Object"));
    }

    [Fact]
    public void Resolve_EmitsCallsEdge_BetweenMethods_WithResolvedConfidence()
    {
        var (nodes, edges) = DiscoverThenResolve(SampleCode);

        var greetMethod = nodes.Single(n => n.NodeType == NodeType.Method && n.Name == "Greet");
        var logMethod = nodes.Single(n => n.NodeType == NodeType.Method && n.Name == "Log");
        var helperMethod = nodes.Single(n => n.NodeType == NodeType.Method && n.Name == "Helper");

        var callToLog = Assert.Single(edges, e => e.SourceId == greetMethod.NodeId && e.TargetId == logMethod.NodeId);
        Assert.Equal(RelationshipType.Calls, callToLog.RelationshipType);
        Assert.Equal(ResolutionConfidenceValues.Resolved, callToLog.Metadata[MetadataKeys.ResolutionConfidence]);

        Assert.Contains(edges, e => e.SourceId == greetMethod.NodeId && e.TargetId == helperMethod.NodeId && e.RelationshipType == RelationshipType.Calls);
    }
}

using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Heuristics.WebApi;

namespace ArchScanner.Core.Tests;

public class MinimalApiDetectorTests
{
    [Fact]
    public void MapGetCallSite_OnIEndpointRouteBuilder_EmitsMinimalApiEndpointNode_LinkedToHandler()
    {
        const string code = """
            namespace Sample
            {
                public static class Program
                {
                    public static void Configure(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                    {
                        app.MapGet("/orders", GetOrders);
                    }

                    public static string GetOrders() => "orders";
                }
            }
            """;

        var (compilation, tree) = TestCompilationFactory.CreateWithFrameworkStubs(code);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var edges = new ConcurrentBag<EdgeDto>();
        var projectNodeId = nodeIdFactory.ForProject("proj1");

        new ArchDeclarationWalker(semanticModel, "proj1", "Test.cs", projectNodeId, nodeIdFactory, registry, idGenerator, nodes, edges)
            .Visit(root);

        MinimalApiDetector.Detect(semanticModel, root, "proj1", idGenerator, registry, nodes, edges);

        var endpoint = Assert.Single(nodes, n => n.NodeType == NodeType.MinimalApiEndpoint);
        Assert.Equal("GET", endpoint.Metadata[MetadataKeys.HttpMethod]);
        Assert.Equal("/orders", endpoint.Metadata[MetadataKeys.RouteTemplate]);

        var handler = nodes.Single(n => n.NodeType == NodeType.Method && n.Name == "GetOrders");
        Assert.Contains(edges, e => e.SourceId == endpoint.NodeId && e.TargetId == handler.NodeId && e.RelationshipType == RelationshipType.Calls);
    }
}

using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Resolution;

namespace ArchScanner.Core.Tests;

public class MessagingResolutionTests
{
    private static (IReadOnlyList<NodeDto> Nodes, IReadOnlyList<EdgeDto> Edges) DiscoverThenResolve(string code)
    {
        var (compilation, tree) = TestCompilationFactory.CreateWithFrameworkStubs(code);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var discoveryEdges = new ConcurrentBag<EdgeDto>();
        var projectNodeId = nodeIdFactory.ForProject("proj1");

        new ArchDeclarationWalker(semanticModel, "proj1", "Test.cs", projectNodeId, nodeIdFactory, registry, idGenerator, nodes, discoveryEdges)
            .Visit(root);

        var relationshipEdges = new ConcurrentBag<EdgeDto>();
        new RelationshipWalker(semanticModel, registry, idGenerator, relationshipEdges).Visit(root);

        return (nodes.ToList(), relationshipEdges.ToList());
    }

    [Fact]
    public void MassTransitConsumer_ImplementingIConsumer_EmitsConsumesEdge_ToMessageType()
    {
        const string code = """
            namespace Sample
            {
                public class OrderCreated { }

                public class OrderCreatedConsumer : MassTransit.IConsumer<OrderCreated> { }
            }
            """;

        var (nodes, edges) = DiscoverThenResolve(code);

        var consumer = nodes.Single(n => n.Name == "OrderCreatedConsumer");
        var message = nodes.Single(n => n.Name == "OrderCreated");

        Assert.Contains(edges, e => e.SourceId == consumer.NodeId && e.TargetId == message.NodeId && e.RelationshipType == RelationshipType.Consumes);
    }

    [Fact]
    public void PublishEndpoint_PublishCallSite_EmitsPublishesEdge_ToMessageType()
    {
        const string code = """
            namespace Sample
            {
                public class OrderCreated { }

                public class OrderService
                {
                    private readonly MassTransit.IPublishEndpoint _publishEndpoint;

                    public OrderService(MassTransit.IPublishEndpoint publishEndpoint)
                    {
                        _publishEndpoint = publishEndpoint;
                    }

                    public void CreateOrder()
                    {
                        _publishEndpoint.Publish(new OrderCreated());
                    }
                }
            }
            """;

        var (nodes, edges) = DiscoverThenResolve(code);

        var service = nodes.Single(n => n.Name == "OrderService");
        var message = nodes.Single(n => n.Name == "OrderCreated");

        var publishes = Assert.Single(edges, e => e.RelationshipType == RelationshipType.Publishes);
        Assert.Equal(service.NodeId, publishes.SourceId);
        Assert.Equal(message.NodeId, publishes.TargetId);
    }
}

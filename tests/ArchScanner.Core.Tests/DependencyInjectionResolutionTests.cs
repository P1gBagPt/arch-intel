using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Heuristics;
using ArchScanner.Core.Resolution;

namespace ArchScanner.Core.Tests;

public class DependencyInjectionResolutionTests
{
    [Fact]
    public void ConstructorParameter_TypedAsInterface_ProducesInjectsEdge_TaggedViaConstructor()
    {
        const string code = """
            namespace Sample
            {
                public interface IOrderRepository { }

                public class OrderRepository : IOrderRepository { }

                public class OrderService
                {
                    public OrderService(IOrderRepository repository) { }
                }
            }
            """;

        var (compilation, tree) = TestCompilationFactory.CreateSingleFile(code);
        var semanticModel = compilation.GetSemanticModel(tree);

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var discoveryEdges = new ConcurrentBag<EdgeDto>();
        var projectNodeId = nodeIdFactory.ForProject("proj1");

        new ArchDeclarationWalker(semanticModel, "proj1", "Test.cs", projectNodeId, nodeIdFactory, registry, idGenerator, nodes, discoveryEdges)
            .Visit(tree.GetRoot());

        var relationshipEdges = new ConcurrentBag<EdgeDto>();
        new RelationshipWalker(semanticModel, registry, idGenerator, relationshipEdges).Visit(tree.GetRoot());

        var service = nodes.ToList().Single(n => n.Name == "OrderService");
        var repositoryInterface = nodes.ToList().Single(n => n.NodeType == NodeType.Interface && n.Name == "IOrderRepository");

        var injects = Assert.Single(relationshipEdges, e => e.RelationshipType == RelationshipType.Injects);
        Assert.Equal(service.NodeId, injects.SourceId);
        Assert.Equal(repositoryInterface.NodeId, injects.TargetId);
        Assert.Equal("true", injects.Metadata[MetadataKeys.ViaConstructor]);
    }

    [Fact]
    public void DiOwnsEdgeResolver_ResolvesInterfaceToConcreteMapping_FromAddScopedCallSite()
    {
        const string code = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Sample
            {
                public interface IOrderRepository { }

                public class OrderRepository : IOrderRepository { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IOrderRepository, OrderRepository>();
                    }
                }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }

                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TInterface, TImplementation>(this IServiceCollection services)
                        where TImplementation : TInterface
                        => services;
                }
            }
            """;

        var (compilation, tree) = TestCompilationFactory.CreateSingleFile(code);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var edges = new ConcurrentBag<EdgeDto>();
        var projectNodeId = nodeIdFactory.ForProject("proj1");

        var signals = ProjectSignalsScanner.Scan([(semanticModel, root)]);
        new ArchDeclarationWalker(semanticModel, "proj1", "Test.cs", projectNodeId, nodeIdFactory, registry, idGenerator, nodes, edges, signals)
            .Visit(root);

        var ownsEdges = DiOwnsEdgeResolver.Resolve(signals, registry, idGenerator);

        var repositoryInterface = nodes.ToList().Single(n => n.NodeType == NodeType.Interface && n.Name == "IOrderRepository");
        var repositoryConcrete = nodes.ToList().Single(n => n.Name == "OrderRepository");

        var owns = Assert.Single(ownsEdges);
        Assert.Equal(repositoryInterface.NodeId, owns.SourceId);
        Assert.Equal(repositoryConcrete.NodeId, owns.TargetId);
        Assert.Equal(RelationshipType.Owns, owns.RelationshipType);
        Assert.Equal("Scoped", owns.Metadata[MetadataKeys.DiLifetime]);
    }
}

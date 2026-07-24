using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Core;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Heuristics;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Tests;

/// <summary>Runs ProjectSignalsScanner + ArchDeclarationWalker over a single-file compilation (with FrameworkStubs), mirroring what DiscoveryPass does per project.</summary>
public static class HeuristicTestHarness
{
    public static (IReadOnlyList<NodeDto> Nodes, IReadOnlyList<EdgeDto> Edges) ClassifyWithStubs(string code, string projectId = "proj1")
    {
        var (compilation, tree) = TestCompilationFactory.CreateWithFrameworkStubs(code);
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();

        var signals = ProjectSignalsScanner.Scan([(semanticModel, root)]);

        var idGenerator = new IdGenerator();
        var nodeIdFactory = new NodeIdFactory(idGenerator);
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var edges = new ConcurrentBag<EdgeDto>();
        var projectNodeId = nodeIdFactory.ForProject(projectId);

        new ArchDeclarationWalker(semanticModel, projectId, "Test.cs", projectNodeId, nodeIdFactory, registry, idGenerator, nodes, edges, signals)
            .Visit(root);

        return (nodes.ToList(), edges.ToList());
    }
}

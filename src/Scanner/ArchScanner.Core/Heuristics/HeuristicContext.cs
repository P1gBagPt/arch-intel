using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchScanner.Core.Discovery;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Heuristics;

/// <summary>
/// Bundles what every heuristic detector needs (Section 3.4): the semantic model/tree for one
/// document, the shared SymbolRegistry (so a heuristic can resolve an edge to a node emitted by
/// Pass 1 or by another heuristic), and the output sinks. Detectors are independent and additive —
/// turning one off must not break another.
/// </summary>
public sealed class HeuristicContext
{
    public required SemanticModel SemanticModel { get; init; }
    public required SyntaxNode Root { get; init; }
    public required string ProjectId { get; init; }
    public required SymbolRegistry Registry { get; init; }
    public required NodeIdFactory NodeIdFactory { get; init; }
    public required IIdGenerator IdGenerator { get; init; }
    public required ConcurrentBag<NodeDto> Nodes { get; init; }
    public required ConcurrentBag<EdgeDto> Edges { get; init; }
}

public interface IHeuristicDetector
{
    void Detect(HeuristicContext context);
}

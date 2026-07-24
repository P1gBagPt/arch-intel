using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Discovery;

/// <summary>Deterministic node-id computation (Section 4.1), delegating the actual hashing to the shared IIdGenerator.</summary>
public sealed class NodeIdFactory
{
    private readonly IIdGenerator _idGenerator;

    public NodeIdFactory(IIdGenerator idGenerator)
    {
        _idGenerator = idGenerator;
    }

    public string ForSymbol(string projectId, string? namespaceName, ISymbol symbol, NodeType nodeType)
        => _idGenerator.NodeId(projectId, namespaceName, GlobalSymbolKey.Compute(symbol), nodeType);

    public string ForNamespace(string projectId, string namespaceName)
        => _idGenerator.NodeId(projectId, namespaceName, GlobalSymbolKey.ForNamespace(projectId, namespaceName), NodeType.Namespace);

    public string ForProject(string projectId)
        => _idGenerator.NodeId(projectId, null, GlobalSymbolKey.ForProject(projectId), NodeType.Project);

    public string ForSolution(string solutionPath)
        => _idGenerator.NodeId("solution", null, GlobalSymbolKey.ForSolution(solutionPath), NodeType.Solution);
}

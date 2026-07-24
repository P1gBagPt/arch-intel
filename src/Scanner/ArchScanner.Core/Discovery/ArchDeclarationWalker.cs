using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Heuristics;
using ArchScanner.Core.Heuristics.WebApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchScanner.Core.Discovery;

/// <summary>
/// Pass 1 (Section 3.3): walks one syntax tree, emitting an ArchNode for every declared symbol
/// (classes, interfaces, records, structs, enums, methods, constructors, properties, fields) plus
/// the structural Contains-edge hierarchy (Project -> Namespace -> Type -> Member). Cross-project
/// relationships are NOT resolved here — that's Pass 2, once every project has finished Pass 1.
///
/// Type/method classification (Section 3.4 — Controller, Repository, MediatRHandler, etc.) is
/// folded in HERE rather than applied as a later pass, because NodeId hashes in the NodeType: a
/// heuristic that reclassified a node after Pass 1 would orphan the original id instead of
/// updating it. <see cref="TypeClassifier"/> decides the final NodeType before it's ever hashed.
/// </summary>
public sealed class ArchDeclarationWalker : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly string _projectId;
    private readonly string _relativeFilePath;
    private readonly NodeIdFactory _nodeIdFactory;
    private readonly SymbolRegistry _registry;
    private readonly IIdGenerator _idGenerator;
    private readonly ProjectSignals _signals;
    private readonly ConcurrentBag<NodeDto> _nodes;
    private readonly ConcurrentBag<EdgeDto> _edges;
    private readonly Stack<string> _containerStack = new();
    private readonly Stack<NodeType> _containerTypeStack = new();

    public ArchDeclarationWalker(
        SemanticModel semanticModel,
        string projectId,
        string relativeFilePath,
        string projectNodeId,
        NodeIdFactory nodeIdFactory,
        SymbolRegistry registry,
        IIdGenerator idGenerator,
        ConcurrentBag<NodeDto> nodes,
        ConcurrentBag<EdgeDto> edges,
        ProjectSignals? signals = null)
        : base(SyntaxWalkerDepth.Node)
    {
        _semanticModel = semanticModel;
        _projectId = projectId;
        _relativeFilePath = relativeFilePath;
        _nodeIdFactory = nodeIdFactory;
        _registry = registry;
        _idGenerator = idGenerator;
        _signals = signals ?? new ProjectSignals();
        _nodes = nodes;
        _edges = edges;
        _containerStack.Push(projectNodeId);
        _containerTypeStack.Push(NodeType.Project);
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        => VisitNamespaceLike(node.Name.ToString(), () => base.VisitNamespaceDeclaration(node));

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        => VisitNamespaceLike(node.Name.ToString(), () => base.VisitFileScopedNamespaceDeclaration(node));

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        => VisitClassifiedTypeDeclaration(node, NodeType.Class, () => base.VisitClassDeclaration(node));

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        => VisitTypeDeclaration(node, NodeType.Interface, new Dictionary<string, string>(), () => base.VisitInterfaceDeclaration(node));

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
        => VisitTypeDeclaration(node, NodeType.Struct, new Dictionary<string, string>(), () => base.VisitStructDeclaration(node));

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var fallback = node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? NodeType.Struct : NodeType.Record;
        VisitClassifiedTypeDeclaration(node, fallback, () => base.VisitRecordDeclaration(node));
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        => VisitTypeDeclaration(node, NodeType.Enum, new Dictionary<string, string>(), () => base.VisitEnumDeclaration(node));

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        => VisitMethodMember(node, () => base.VisitMethodDeclaration(node));

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        => VisitMember(node, NodeType.Constructor, new Dictionary<string, string>(), () => base.VisitConstructorDeclaration(node));

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        => VisitMember(node, NodeType.Property, new Dictionary<string, string>(), () => base.VisitPropertyDeclaration(node));

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        foreach (var variable in node.Declaration.Variables)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(variable);
            if (symbol is not null)
            {
                EmitSymbolNode(symbol, NodeType.Field, new Dictionary<string, string>(), variable);
            }
        }

        base.VisitFieldDeclaration(node);
    }

    private void VisitNamespaceLike(string namespaceName, Action visitChildren)
    {
        var namespaceNodeId = _nodeIdFactory.ForNamespace(_projectId, namespaceName);
        if (_registry.TryRegister(GlobalSymbolKey.ForNamespace(_projectId, namespaceName), namespaceNodeId))
        {
            _nodes.Add(new NodeDto
            {
                NodeId = namespaceNodeId,
                ProjectId = _projectId,
                NodeType = NodeType.Namespace,
                Name = namespaceName.Contains('.') ? namespaceName[(namespaceName.LastIndexOf('.') + 1)..] : namespaceName,
                FullName = namespaceName,
                Namespace = namespaceName,
            });
        }

        EmitContainsEdge(_containerStack.Peek(), namespaceNodeId);

        _containerStack.Push(namespaceNodeId);
        _containerTypeStack.Push(NodeType.Namespace);
        try
        {
            visitChildren();
        }
        finally
        {
            _containerStack.Pop();
            _containerTypeStack.Pop();
        }
    }

    private void VisitClassifiedTypeDeclaration(SyntaxNode node, NodeType fallback, Action visitChildren)
    {
        var declaredSymbol = _semanticModel.GetDeclaredSymbol(node) as INamedTypeSymbol;
        var (nodeType, metadata) = declaredSymbol is not null
            ? TypeClassifier.Classify(declaredSymbol, _signals, fallback)
            : (fallback, new Dictionary<string, string>());

        VisitTypeDeclaration(node, nodeType, metadata, visitChildren);
    }

    private void VisitTypeDeclaration(SyntaxNode node, NodeType nodeType, Dictionary<string, string> metadata, Action visitChildren)
    {
        var symbol = _semanticModel.GetDeclaredSymbol(node);
        if (symbol is null)
        {
            visitChildren();
            return;
        }

        var nodeId = EmitSymbolNode(symbol, nodeType, metadata, node);

        _containerStack.Push(nodeId);
        _containerTypeStack.Push(nodeType);
        try
        {
            visitChildren();
        }
        finally
        {
            _containerStack.Pop();
            _containerTypeStack.Pop();
        }
    }

    private void VisitMethodMember(MethodDeclarationSyntax node, Action visitChildren)
    {
        var symbol = _semanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
        if (symbol is null)
        {
            visitChildren();
            return;
        }

        var nodeType = TypeClassifier.IsTestMethodSymbol(symbol) ? NodeType.TestMethod : NodeType.Method;
        var metadata = _containerTypeStack.Peek() == NodeType.Controller
            ? ControllerActionMetadata.Extract(symbol)
            : new Dictionary<string, string>();

        EmitSymbolNode(symbol, nodeType, metadata, node);
        visitChildren();
    }

    private void VisitMember(SyntaxNode node, NodeType nodeType, Dictionary<string, string> metadata, Action visitChildren)
    {
        var symbol = _semanticModel.GetDeclaredSymbol(node);
        if (symbol is not null)
        {
            EmitSymbolNode(symbol, nodeType, metadata, node);
        }

        visitChildren();
    }

    private string EmitSymbolNode(ISymbol symbol, NodeType nodeType, Dictionary<string, string> metadata, SyntaxNode node)
    {
        var namespaceName = symbol.ContainingNamespace?.IsGlobalNamespace == false
            ? symbol.ContainingNamespace.ToDisplayString()
            : null;

        var nodeId = _nodeIdFactory.ForSymbol(_projectId, namespaceName, symbol, nodeType);

        if (_registry.TryRegister(GlobalSymbolKey.Compute(symbol), nodeId))
        {
            var lineSpan = node.GetLocation().GetLineSpan();

            _nodes.Add(new NodeDto
            {
                NodeId = nodeId,
                ProjectId = _projectId,
                NodeType = nodeType,
                Name = symbol.Name,
                FullName = GlobalSymbolKey.DisplayName(symbol),
                Namespace = namespaceName,
                FilePath = _relativeFilePath,
                LineStart = lineSpan.StartLinePosition.Line + 1,
                LineEnd = lineSpan.EndLinePosition.Line + 1,
                Metadata = metadata,
            });
        }

        EmitContainsEdge(_containerStack.Peek(), nodeId);
        return nodeId;
    }

    private void EmitContainsEdge(string sourceId, string targetId)
    {
        if (sourceId == targetId)
        {
            return;
        }

        _edges.Add(new EdgeDto
        {
            EdgeId = _idGenerator.EdgeId(sourceId, targetId, RelationshipType.Contains),
            SourceId = sourceId,
            TargetId = targetId,
            RelationshipType = RelationshipType.Contains,
        });
    }
}

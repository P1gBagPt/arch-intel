using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Heuristics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchScanner.Core.Resolution;

/// <summary>
/// Pass 2 (Section 3.3): re-walks a syntax tree already processed by Pass 1, resolving
/// Implements/Inherits/Calls edges against the now-fully-populated SymbolRegistry. Must not run
/// for any project until Pass 1 has completed for every project (Risk #10) — a symbol declared in
/// a project scanned later would otherwise resolve as "unresolved" purely due to timing.
/// </summary>
public sealed class RelationshipWalker : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly SymbolRegistry _registry;
    private readonly IIdGenerator _idGenerator;
    private readonly ConcurrentBag<EdgeDto> _edges;
    private ISymbol? _currentMember;

    public RelationshipWalker(SemanticModel semanticModel, SymbolRegistry registry, IIdGenerator idGenerator, ConcurrentBag<EdgeDto> edges)
        : base(SyntaxWalkerDepth.Node)
    {
        _semanticModel = semanticModel;
        _registry = registry;
        _idGenerator = idGenerator;
        _edges = edges;
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        ResolveTypeRelationships(node);
        base.VisitClassDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        ResolveTypeRelationships(node);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        ResolveTypeRelationships(node);
        base.VisitStructDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        ResolveTypeRelationships(node);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        => VisitMemberBody(node, () => base.VisitMethodDeclaration(node));

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        ResolveConstructorInjects(node);
        VisitMemberBody(node, () => base.VisitConstructorDeclaration(node));
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        => VisitMemberBody(node, () => base.VisitPropertyDeclaration(node));

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        ResolveCall(node);
        ResolvePublish(node);
        base.VisitInvocationExpression(node);
    }

    private void VisitMemberBody(SyntaxNode node, Action visitChildren)
    {
        var symbol = _semanticModel.GetDeclaredSymbol(node);
        var previous = _currentMember;
        _currentMember = symbol ?? previous;
        try
        {
            visitChildren();
        }
        finally
        {
            _currentMember = previous;
        }
    }

    private void ResolveTypeRelationships(SyntaxNode node)
    {
        if (_semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
        {
            return;
        }

        if (!_registry.TryGetNodeId(GlobalSymbolKey.Compute(typeSymbol), out var sourceId))
        {
            return;
        }

        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (_registry.TryGetNodeId(GlobalSymbolKey.Compute(iface), out var targetId))
            {
                EmitEdge(sourceId, targetId, RelationshipType.Implements, ResolutionConfidenceValues.Resolved);
            }
        }

        var baseType = typeSymbol.BaseType;
        while (baseType is not null && baseType.SpecialType != SpecialType.System_Object)
        {
            if (_registry.TryGetNodeId(GlobalSymbolKey.Compute(baseType), out var targetId))
            {
                EmitEdge(sourceId, targetId, RelationshipType.Inherits, ResolutionConfidenceValues.Resolved);
            }

            baseType = baseType.BaseType;
        }

        // MassTransit consumer -> message type (Section 3.4, Message queues row).
        var consumerInterface = SymbolMatching.FindConstructedInterface(typeSymbol, "MassTransit.IConsumer<TMessage>");
        if (consumerInterface is { TypeArguments: [INamedTypeSymbol messageType] }
            && _registry.TryGetNodeId(GlobalSymbolKey.Compute(messageType), out var messageNodeId))
        {
            EmitEdge(sourceId, messageNodeId, RelationshipType.Consumes, ResolutionConfidenceValues.Resolved);
        }
    }

    private void ResolvePublish(InvocationExpressionSyntax node)
    {
        if (_currentMember?.ContainingType is not INamedTypeSymbol containingType)
        {
            return;
        }

        if (node.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Publish" or "Send" })
        {
            return;
        }

        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method || method.ReceiverType is null)
        {
            return;
        }

        var receiverFqn = method.ReceiverType.OriginalDefinition.ToDisplayString();
        if (receiverFqn is not ("MassTransit.IPublishEndpoint" or "MassTransit.IBus"))
        {
            return;
        }

        var messageType = method.TypeArguments.Length == 1
            ? method.TypeArguments[0]
            : node.ArgumentList.Arguments.Count > 0
                ? _semanticModel.GetTypeInfo(node.ArgumentList.Arguments[0].Expression).Type
                : null;

        if (messageType is not INamedTypeSymbol namedMessageType
            || !_registry.TryGetNodeId(GlobalSymbolKey.Compute(containingType), out var sourceId)
            || !_registry.TryGetNodeId(GlobalSymbolKey.Compute(namedMessageType), out var targetId))
        {
            return;
        }

        EmitEdge(sourceId, targetId, RelationshipType.Publishes, ResolutionConfidenceValues.Heuristic);
    }

    private void ResolveConstructorInjects(ConstructorDeclarationSyntax node)
    {
        // Independent of DI-registration detection (Section 3.4): every constructor parameter
        // whose type is an interface or class produces an Injects edge from the containing class,
        // regardless of whether that dependency is ever registered with a DI container.
        if (_semanticModel.GetDeclaredSymbol(node) is not IMethodSymbol { ContainingType: { } containingType } ctorSymbol)
        {
            return;
        }

        if (!_registry.TryGetNodeId(GlobalSymbolKey.Compute(containingType), out var sourceId))
        {
            return;
        }

        foreach (var parameter in ctorSymbol.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Interface or TypeKind.Class } paramType)
            {
                continue;
            }

            if (!_registry.TryGetNodeId(GlobalSymbolKey.Compute(paramType), out var targetId) || sourceId == targetId)
            {
                continue;
            }

            _edges.Add(new EdgeDto
            {
                EdgeId = _idGenerator.EdgeId(sourceId, targetId, RelationshipType.Injects),
                SourceId = sourceId,
                TargetId = targetId,
                RelationshipType = RelationshipType.Injects,
                Metadata = new Dictionary<string, string> { [MetadataKeys.ViaConstructor] = "true" },
            });
        }
    }

    private void ResolveCall(InvocationExpressionSyntax node)
    {
        if (_currentMember is null || !_registry.TryGetNodeId(GlobalSymbolKey.Compute(_currentMember), out var sourceId))
        {
            return;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        var confidence = ResolutionConfidenceValues.Resolved;
        var targetMethod = symbolInfo.Symbol as IMethodSymbol;

        if (targetMethod is null && symbolInfo.CandidateSymbols.Length > 0)
        {
            // Overload resolution failure (often a load error): record with the first candidate
            // rather than dropping the edge (Section 3.3).
            targetMethod = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
            confidence = ResolutionConfidenceValues.Heuristic;
        }

        if (targetMethod is null)
        {
            return;
        }

        if (_registry.TryGetNodeId(GlobalSymbolKey.Compute(targetMethod), out var targetId))
        {
            EmitEdge(sourceId, targetId, RelationshipType.Calls, confidence);
        }
    }

    private void EmitEdge(string sourceId, string targetId, RelationshipType type, string confidence)
    {
        if (sourceId == targetId)
        {
            return;
        }

        _edges.Add(new EdgeDto
        {
            EdgeId = _idGenerator.EdgeId(sourceId, targetId, type),
            SourceId = sourceId,
            TargetId = targetId,
            RelationshipType = type,
            Metadata = new Dictionary<string, string> { [MetadataKeys.ResolutionConfidence] = confidence },
        });
    }
}

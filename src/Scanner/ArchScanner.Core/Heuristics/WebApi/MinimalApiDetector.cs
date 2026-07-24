using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Discovery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchScanner.Core.Heuristics.WebApi;

/// <summary>
/// Syntax-level scan (Section 3.4, Minimal APIs row) for `app.MapGet("/route", Handler)`-style
/// calls on an <c>IEndpointRouteBuilder</c> receiver. Unlike controller actions, there's no
/// existing declaration for a minimal API endpoint — the endpoint IS the invocation — so this
/// always creates a new node rather than reclassifying one Pass 1 already emitted.
/// </summary>
public static class MinimalApiDetector
{
    private static readonly Dictionary<string, string> MapMethodToHttpVerb = new()
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH",
    };

    public static void Detect(
        SemanticModel semanticModel,
        SyntaxNode root,
        string projectId,
        IIdGenerator idGenerator,
        SymbolRegistry registry,
        ConcurrentBag<NodeDto> nodes,
        ConcurrentBag<EdgeDto> edges)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (!MapMethodToHttpVerb.TryGetValue(methodName, out var httpVerb))
            {
                continue;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (receiverType is null || !ImplementsEndpointRouteBuilder(receiverType))
            {
                continue;
            }

            var routeTemplate = invocation.ArgumentList.Arguments.Count > 0
                && semanticModel.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression) is { HasValue: true, Value: string route }
                ? route
                : "(unknown)";

            var endpointNodeId = idGenerator.NodeId(projectId, null, $"{httpVerb}:{routeTemplate}", NodeType.MinimalApiEndpoint);

            if (registry.TryRegister($"{projectId}::endpoint::{httpVerb}:{routeTemplate}", endpointNodeId))
            {
                nodes.Add(new NodeDto
                {
                    NodeId = endpointNodeId,
                    ProjectId = projectId,
                    NodeType = NodeType.MinimalApiEndpoint,
                    Name = $"{httpVerb} {routeTemplate}",
                    FullName = $"{httpVerb} {routeTemplate}",
                    Metadata = new Dictionary<string, string>
                    {
                        [MetadataKeys.HttpMethod] = httpVerb,
                        [MetadataKeys.RouteTemplate] = routeTemplate,
                    },
                });
            }

            if (invocation.ArgumentList.Arguments.Count > 1)
            {
                var handlerArg = invocation.ArgumentList.Arguments[1].Expression;
                var handlerSymbolInfo = semanticModel.GetSymbolInfo(handlerArg);
                // A method group passed where a non-generic `Delegate` parameter is expected (the
                // real ASP.NET Core MapGet signature) resolves via "natural type" inference rather
                // than classic delegate-conversion overload matching, so .Symbol is often empty
                // with the method surfaced as the sole candidate instead.
                var handlerMethod = handlerSymbolInfo.Symbol as IMethodSymbol
                    ?? handlerSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

                if (handlerMethod is not null && registry.TryGetNodeId(GlobalSymbolKey.Compute(handlerMethod), out var handlerNodeId))
                {
                    edges.Add(new EdgeDto
                    {
                        EdgeId = idGenerator.EdgeId(endpointNodeId, handlerNodeId, RelationshipType.Calls),
                        SourceId = endpointNodeId,
                        TargetId = handlerNodeId,
                        RelationshipType = RelationshipType.Calls,
                    });
                }
            }
        }
    }

    private static bool ImplementsEndpointRouteBuilder(ITypeSymbol type)
        => type.ToDisplayString() == "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"
        || type.AllInterfaces.Any(i => i.ToDisplayString() == "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");
}

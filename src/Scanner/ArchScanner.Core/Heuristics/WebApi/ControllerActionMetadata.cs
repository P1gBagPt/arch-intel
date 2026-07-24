using ArchIntel.GraphStore.Contracts;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Heuristics.WebApi;

/// <summary>
/// Extracts httpMethod/routeTemplate metadata for a controller action method (Section 3.4,
/// Controllers row). 02's NodeType enum has no dedicated node type for a controller action
/// (only MinimalApiEndpoint, which is a syntax-level Map* call, not a declaration) — so the
/// action stays a Method node, with the HTTP verb/route captured as metadata instead.
/// </summary>
public static class ControllerActionMetadata
{
    private static readonly (string AttributeName, string? HttpMethod)[] RouteAttributes =
    [
        ("Microsoft.AspNetCore.Mvc.HttpGetAttribute", "GET"),
        ("Microsoft.AspNetCore.Mvc.HttpPostAttribute", "POST"),
        ("Microsoft.AspNetCore.Mvc.HttpPutAttribute", "PUT"),
        ("Microsoft.AspNetCore.Mvc.HttpDeleteAttribute", "DELETE"),
        ("Microsoft.AspNetCore.Mvc.HttpPatchAttribute", "PATCH"),
        ("Microsoft.AspNetCore.Mvc.RouteAttribute", null),
    ];

    public static Dictionary<string, string> Extract(IMethodSymbol method)
    {
        var metadata = new Dictionary<string, string>();

        foreach (var (attributeName, httpMethod) in RouteAttributes)
        {
            var attribute = SymbolMatching.GetAttribute(method, attributeName);
            if (attribute is null)
            {
                continue;
            }

            if (httpMethod is not null)
            {
                metadata[MetadataKeys.HttpMethod] = httpMethod;
            }

            var routeArg = attribute.ConstructorArguments.FirstOrDefault(a => a.Kind == TypedConstantKind.Primitive && a.Value is string);
            if (routeArg.Value is string route)
            {
                metadata[MetadataKeys.RouteTemplate] = route;
            }

            break;
        }

        return metadata;
    }
}

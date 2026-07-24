using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Heuristics;

/// <summary>Shared FQN-matching helpers so each detector doesn't reimplement symbol-walking.</summary>
public static class SymbolMatching
{
    public static bool ImplementsInterface(INamedTypeSymbol type, string fullyQualifiedInterfaceName)
        => type.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == fullyQualifiedInterfaceName);

    /// <summary>Finds the first implemented interface whose *unbound* generic definition matches, e.g. "MediatR.IRequestHandler&lt;TRequest, TResponse&gt;".</summary>
    public static INamedTypeSymbol? FindConstructedInterface(INamedTypeSymbol type, string unboundGenericInterfaceName)
        => type.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() == unboundGenericInterfaceName);

    public static bool InheritsFrom(INamedTypeSymbol type, string fullyQualifiedBaseName)
    {
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (baseType.OriginalDefinition.ToDisplayString() == fullyQualifiedBaseName)
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    public static bool HasAttribute(ISymbol symbol, string fullyQualifiedAttributeName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString() == fullyQualifiedAttributeName);

    public static AttributeData? GetAttribute(ISymbol symbol, string fullyQualifiedAttributeName)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.OriginalDefinition.ToDisplayString() == fullyQualifiedAttributeName);
}

using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Discovery;

/// <summary>
/// Computes a solution-wide symbol identity that's stable regardless of which project's
/// compilation produced the symbol (Section 3.3) — a type declared in Domain is a *different*
/// INamedTypeSymbol instance in Domain's own compilation vs. seen through a CompilationReference
/// from Infrastructure, so reference-equality can't be used as the identity.
/// </summary>
public static class GlobalSymbolKey
{
    private static readonly SymbolDisplayFormat FullyQualifiedNoGlobalPrefix =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    public static string Compute(ISymbol symbol)
    {
        var fqn = symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var assemblyName = symbol.ContainingAssembly?.Name ?? string.Empty;
        return $"{assemblyName}::{fqn}";
    }

    /// <summary>Display name without the "global::" prefix — used for NodeDto.FullName (human-facing).</summary>
    public static string DisplayName(ISymbol symbol) => symbol.ToDisplayString(FullyQualifiedNoGlobalPrefix);

    public static string ForNamespace(string projectId, string namespaceName) => $"{projectId}::namespace::{namespaceName}";

    public static string ForProject(string projectId) => $"{projectId}::project";

    public static string ForSolution(string solutionPath) => $"solution::{solutionPath}";
}

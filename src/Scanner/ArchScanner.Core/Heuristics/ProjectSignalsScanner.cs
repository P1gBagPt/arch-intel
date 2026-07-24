using ArchScanner.Core.Discovery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchScanner.Core.Heuristics;

/// <summary>
/// Pre-pass (runs before ArchDeclarationWalker for a project): scans every document's semantic
/// model for signals that reclassify some OTHER type (DbSet&lt;T&gt; membership, IOptions&lt;T&gt;
/// unwrapping, DI registrations) so Pass 1 can assign the final NodeType once, up front, instead
/// of retrofitting it after a node id has already been computed from a (now wrong) NodeType.
/// </summary>
public static class ProjectSignalsScanner
{
    public static ProjectSignals Scan(IEnumerable<(SemanticModel SemanticModel, SyntaxNode Root)> documents)
    {
        var signals = new ProjectSignals();

        foreach (var (semanticModel, root) in documents)
        {
            ScanEfEntities(semanticModel, root, signals);
            ScanConfigurationBindings(semanticModel, root, signals);
            ScanDiRegistrations(semanticModel, root, signals);
        }

        return signals;
    }

    private static void ScanEfEntities(SemanticModel semanticModel, SyntaxNode root, ProjectSignals signals)
    {
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol type)
            {
                continue;
            }

            if (!SymbolMatching.InheritsFrom(type, "Microsoft.EntityFrameworkCore.DbContext"))
            {
                continue;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.Type is INamedTypeSymbol { OriginalDefinition.Name: "DbSet" } dbSetType
                    && dbSetType.TypeArguments.Length == 1
                    && dbSetType.TypeArguments[0] is INamedTypeSymbol entityType)
                {
                    signals.EfEntityGlobalKeys.Add(GlobalSymbolKey.Compute(entityType));
                }
            }
        }
    }

    private static void ScanConfigurationBindings(SemanticModel semanticModel, SyntaxNode root, ProjectSignals signals)
    {
        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(parameter) is not IParameterSymbol { Type: INamedTypeSymbol paramType })
            {
                continue;
            }

            if (paramType.OriginalDefinition.ToDisplayString() is
                    "Microsoft.Extensions.Options.IOptions<TOptions>" or
                    "Microsoft.Extensions.Options.IOptionsSnapshot<TOptions>" or
                    "Microsoft.Extensions.Options.IOptionsMonitor<TOptions>"
                && paramType.TypeArguments.Length == 1
                && paramType.TypeArguments[0] is INamedTypeSymbol settingsType)
            {
                signals.ConfigurationSettingGlobalKeys.Add(GlobalSymbolKey.Compute(settingsType));
            }
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (method.Name == "Configure" && method.TypeArguments.Length == 1 && method.TypeArguments[0] is INamedTypeSymbol configuredType)
            {
                var key = GlobalSymbolKey.Compute(configuredType);
                signals.ConfigurationSettingGlobalKeys.Add(key);
                TryCaptureSectionName(invocation, semanticModel, signals, key);
            }
            else if (method.Name == "Bind" && method.TypeArguments.Length == 1 && method.TypeArguments[0] is INamedTypeSymbol boundType)
            {
                signals.ConfigurationSettingGlobalKeys.Add(GlobalSymbolKey.Compute(boundType));
            }
        }
    }

    private static void TryCaptureSectionName(InvocationExpressionSyntax configureCall, SemanticModel semanticModel, ProjectSignals signals, string key)
    {
        // Looks for the common `configuration.GetSection("X").Configure<T>(...)` chain.
        if (configureCall.Expression is not MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax getSectionCall })
        {
            return;
        }

        if (getSectionCall.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "GetSection" })
        {
            return;
        }

        var sectionArg = getSectionCall.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (sectionArg is not null && semanticModel.GetConstantValue(sectionArg) is { HasValue: true, Value: string sectionName })
        {
            signals.ConfigSectionByGlobalKey[key] = sectionName;
        }
    }

    private static void ScanDiRegistrations(SemanticModel semanticModel, SyntaxNode root, ProjectSignals signals)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (method.Name is not ("AddScoped" or "AddTransient" or "AddSingleton"))
            {
                continue;
            }

            var lifetime = method.Name.Replace("Add", string.Empty, StringComparison.Ordinal);

            if (method.TypeArguments.Length == 2
                && method.TypeArguments[0] is INamedTypeSymbol iface
                && method.TypeArguments[1] is INamedTypeSymbol concrete)
            {
                var ifaceKey = GlobalSymbolKey.Compute(iface);
                signals.DiRegisteredInterfaceLifetimes[ifaceKey] = lifetime;
                signals.DiRegisteredConcreteToInterfaceName[GlobalSymbolKey.Compute(concrete)] = iface.Name;
                signals.DiInterfaceToConcreteGlobalKey[ifaceKey] = GlobalSymbolKey.Compute(concrete);
            }
            else if (method.TypeArguments.Length == 1 && method.TypeArguments[0] is INamedTypeSymbol onlyConcrete)
            {
                // AddScoped<TConcrete>() — self-registration, still a useful Service/Repository signal.
                signals.DiRegisteredConcreteToInterfaceName.TryAdd(GlobalSymbolKey.Compute(onlyConcrete), string.Empty);
            }
            else if (invocation.ArgumentList.Arguments.Count == 2)
            {
                // Non-generic services.AddScoped(typeof(IFoo), typeof(Foo)) form.
                var types = invocation.ArgumentList.Arguments
                    .Select(a => a.Expression)
                    .OfType<TypeOfExpressionSyntax>()
                    .Select(t => semanticModel.GetTypeInfo(t.Type).Type as INamedTypeSymbol)
                    .ToList();

                if (types.Count == 2 && types[0] is not null && types[1] is not null)
                {
                    var ifaceKey = GlobalSymbolKey.Compute(types[0]!);
                    signals.DiRegisteredInterfaceLifetimes[ifaceKey] = lifetime;
                    signals.DiRegisteredConcreteToInterfaceName[GlobalSymbolKey.Compute(types[1]!)] = types[0]!.Name;
                    signals.DiInterfaceToConcreteGlobalKey[ifaceKey] = GlobalSymbolKey.Compute(types[1]!);
                }
            }
        }
    }
}

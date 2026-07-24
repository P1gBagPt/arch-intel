using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Discovery;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Heuristics;

/// <summary>
/// Decides the final, specialized NodeType for a class/record declaration (Section 3.4), checked
/// in priority order. This MUST run before the node's id is computed (nodeId hashes in the
/// NodeType) — reclassifying after the fact would silently orphan the original node's id instead
/// of updating it.
/// </summary>
public static class TypeClassifier
{
    public static (NodeType NodeType, Dictionary<string, string> Metadata) Classify(INamedTypeSymbol type, ProjectSignals signals, NodeType fallback)
    {
        var metadata = new Dictionary<string, string>();

        if (SymbolMatching.InheritsFrom(type, "Microsoft.EntityFrameworkCore.DbContext"))
        {
            return (NodeType.EfDbContext, metadata);
        }

        if (signals.EfEntityGlobalKeys.Contains(GlobalSymbolKey.Compute(type)))
        {
            return (NodeType.EfEntity, metadata);
        }

        if (signals.ConfigurationSettingGlobalKeys.Contains(GlobalSymbolKey.Compute(type)))
        {
            if (signals.ConfigSectionByGlobalKey.TryGetValue(GlobalSymbolKey.Compute(type), out var section))
            {
                metadata[MetadataKeys.ConfigSection] = section;
            }

            return (NodeType.ConfigurationSection, metadata);
        }

        if (SymbolMatching.InheritsFrom(type, "Microsoft.AspNetCore.Mvc.ControllerBase")
            || SymbolMatching.InheritsFrom(type, "Microsoft.AspNetCore.Mvc.Controller")
            || SymbolMatching.HasAttribute(type, "Microsoft.AspNetCore.Mvc.ApiControllerAttribute"))
        {
            return (NodeType.Controller, metadata);
        }

        if (SymbolMatching.InheritsFrom(type, "Microsoft.Extensions.Hosting.BackgroundService"))
        {
            return (NodeType.BackgroundWorker, metadata);
        }

        if (SymbolMatching.ImplementsInterface(type, "Microsoft.Extensions.Hosting.IHostedService"))
        {
            return (NodeType.HostedService, metadata);
        }

        var handlerInterface = FindMediatRHandlerInterface(type);
        if (handlerInterface is not null)
        {
            metadata["mediatRInterface"] = handlerInterface.OriginalDefinition.ToDisplayString();
            return (NodeType.MediatRHandler, metadata);
        }

        if (SymbolMatching.ImplementsInterface(type, "MediatR.INotification"))
        {
            var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var namingMatches = type.Name.EndsWith("Event", StringComparison.Ordinal)
                || type.Name.EndsWith("DomainEvent", StringComparison.Ordinal)
                || ns.EndsWith(".Events", StringComparison.Ordinal)
                || ns.EndsWith(".DomainEvents", StringComparison.Ordinal);
            metadata[MetadataKeys.NamingConventionMatch] = namingMatches ? "true" : "false";
            return (NodeType.DomainEvent, metadata);
        }

        if (type.GetMembers().OfType<IMethodSymbol>().Any(IsTestMethodSymbol))
        {
            return (NodeType.TestClass, metadata);
        }

        if (TryClassifyRepositoryOrService(type, signals, "Repository", NodeType.Repository, out var repoConfidence))
        {
            metadata[MetadataKeys.DetectionConfidence] = repoConfidence;
            return (NodeType.Repository, metadata);
        }

        if (TryClassifyRepositoryOrService(type, signals, "Service", NodeType.Service, out var serviceConfidence))
        {
            metadata[MetadataKeys.DetectionConfidence] = serviceConfidence;
            return (NodeType.Service, metadata);
        }

        return (fallback, metadata);
    }

    public static bool IsTestMethodSymbol(IMethodSymbol method)
        => SymbolMatching.HasAttribute(method, "Xunit.FactAttribute")
        || SymbolMatching.HasAttribute(method, "Xunit.TheoryAttribute")
        || SymbolMatching.HasAttribute(method, "NUnit.Framework.TestAttribute")
        || SymbolMatching.HasAttribute(method, "NUnit.Framework.TestCaseAttribute")
        || SymbolMatching.HasAttribute(method, "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");

    private static INamedTypeSymbol? FindMediatRHandlerInterface(INamedTypeSymbol type)
        => type.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() is
            "MediatR.IRequestHandler<TRequest, TResponse>" or
            "MediatR.IRequestHandler<TRequest>" or
            "MediatR.INotificationHandler<TNotification>");

    private static bool TryClassifyRepositoryOrService(INamedTypeSymbol type, ProjectSignals signals, string suffix, NodeType nodeType, out string confidence)
    {
        var nameMatches = type.Name.EndsWith(suffix, StringComparison.Ordinal);
        var implementsMatchingInterface = type.AllInterfaces.Any(i => i.Name == "I" + type.Name)
            || type.AllInterfaces.Any(i => i.Name.StartsWith('I') && i.Name.EndsWith(suffix, StringComparison.Ordinal));

        var isDiRegistered = signals.DiRegisteredConcreteToInterfaceName.TryGetValue(GlobalSymbolKey.Compute(type), out var registeredInterfaceName);
        var registeredInterfaceMatchesSuffix = registeredInterfaceName?.EndsWith(suffix, StringComparison.Ordinal) ?? false;

        if (nameMatches && implementsMatchingInterface)
        {
            confidence = "NamingAndInterface";
            return true;
        }

        if (isDiRegistered && (implementsMatchingInterface || registeredInterfaceMatchesSuffix))
        {
            confidence = "DIRegistrationOnly";
            return true;
        }

        if (nameMatches)
        {
            confidence = "NamingOnly";
            return true;
        }

        confidence = string.Empty;
        return false;
    }
}

using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Heuristics;

namespace ArchScanner.Core.Resolution;

/// <summary>
/// Turns DI registration signals (AddXxx&lt;TInterface,TConcrete&gt;() call sites, Section 3.4)
/// into Owns edges — the interface -&gt; concrete-implementation mapping constructor injection
/// alone can't reveal (you can see *that* something implementing IOrderRepository is injected,
/// not *which* concrete type, without the registration).
/// </summary>
public static class DiOwnsEdgeResolver
{
    public static IReadOnlyList<EdgeDto> Resolve(ProjectSignals signals, SymbolRegistry registry, IIdGenerator idGenerator)
    {
        var edges = new List<EdgeDto>();

        foreach (var (ifaceKey, concreteKey) in signals.DiInterfaceToConcreteGlobalKey)
        {
            if (!registry.TryGetNodeId(ifaceKey, out var ifaceNodeId) || !registry.TryGetNodeId(concreteKey, out var concreteNodeId))
            {
                continue;
            }

            if (ifaceNodeId == concreteNodeId)
            {
                continue;
            }

            var metadata = signals.DiRegisteredInterfaceLifetimes.TryGetValue(ifaceKey, out var lifetime)
                ? new Dictionary<string, string> { [MetadataKeys.DiLifetime] = lifetime }
                : new Dictionary<string, string>();

            edges.Add(new EdgeDto
            {
                EdgeId = idGenerator.EdgeId(ifaceNodeId, concreteNodeId, RelationshipType.Owns),
                SourceId = ifaceNodeId,
                TargetId = concreteNodeId,
                RelationshipType = RelationshipType.Owns,
                Metadata = metadata,
            });
        }

        return edges;
    }
}

using System.Collections.Concurrent;

namespace ArchScanner.Core.Discovery;

/// <summary>
/// Thread-safe global-symbol-key -> node-id map, shared across all projects during a parallel
/// Pass 1 (Section 3.3). Partial classes naturally de-dupe here since Roslyn's GetDeclaredSymbol
/// already returns one merged symbol for all partial declarations.
/// </summary>
public sealed class SymbolRegistry
{
    private readonly ConcurrentDictionary<string, string> _keyToNodeId = new();

    /// <summary>
    /// Registers nodeId under globalSymbolKey if not already present. Returns true if this call
    /// won the race (first registration) — callers use this to decide whether to emit the NodeDto,
    /// since a second partial-class declaration shouldn't produce a duplicate node.
    /// </summary>
    public bool TryRegister(string globalSymbolKey, string nodeId)
        => _keyToNodeId.TryAdd(globalSymbolKey, nodeId);

    public bool TryGetNodeId(string globalSymbolKey, out string nodeId)
        => _keyToNodeId.TryGetValue(globalSymbolKey, out nodeId!);

    public IReadOnlyDictionary<string, string> Snapshot() => _keyToNodeId;
}

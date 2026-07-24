using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.GraphStore.Contracts;

/// <summary>Deterministic ID generation so re-scans are naturally idempotent.</summary>
public interface IIdGenerator
{
    string NodeId(string projectId, string? @namespace, string fullName, NodeType nodeType);
    string EdgeId(string sourceId, string targetId, RelationshipType relationshipType);
    string ProjectId(string solutionPath, string projectPath);
}

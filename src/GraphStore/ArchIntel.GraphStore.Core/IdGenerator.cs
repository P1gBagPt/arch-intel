using System.Security.Cryptography;
using System.Text;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.GraphStore.Core;

/// <summary>SHA-1 based deterministic ID generation so re-scans are naturally idempotent (never Guid.NewGuid()).</summary>
public sealed class IdGenerator : IIdGenerator
{
    public string NodeId(string projectId, string? @namespace, string fullName, NodeType nodeType)
        => Hash(projectId, @namespace ?? string.Empty, fullName, nodeType.ToString());

    public string EdgeId(string sourceId, string targetId, RelationshipType relationshipType)
        => Hash(sourceId, targetId, relationshipType.ToString());

    public string ProjectId(string solutionPath, string projectPath)
        => Hash(solutionPath, projectPath);

    private static string Hash(params ReadOnlySpan<string> parts)
    {
        var composite = string.Join('|', parts.ToArray());
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(composite));
        return Convert.ToHexStringLower(bytes);
    }
}

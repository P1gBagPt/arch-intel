using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite;

public sealed class SqliteGraphReader : IGraphReader
{
    private const string NodeColumns = """
        node_id AS NodeId, repo_id AS RepoId, project_id AS ProjectId, node_type AS NodeType,
        name AS Name, full_name AS FullName, namespace AS Namespace, file_path AS FilePath,
        line_start AS LineStart, line_end AS LineEnd, is_external AS IsExternal, metadata_json AS Metadata
        """;

    // Qualified with "e." since these are always selected alongside a joined nodes row (n.*).
    private const string EdgeColumnsJoined = """
        e.edge_id AS EdgeId, e.repo_id AS RepoId, e.source_id AS SourceId, e.target_id AS TargetId,
        e.relationship_type AS RelationshipType, e.metadata_json AS Metadata
        """;

    // Qualified with "n." and aliased to the exact NodeDto property names, so Dapper's
    // QueryAsync<EdgeDto, NodeDto, T> multi-map can split the row on the "NodeId" boundary column.
    private const string JoinedNodeColumns = """
        n.node_id AS NodeId, n.repo_id AS RepoId, n.project_id AS ProjectId, n.node_type AS NodeType,
        n.name AS Name, n.full_name AS FullName, n.namespace AS Namespace, n.file_path AS FilePath,
        n.line_start AS LineStart, n.line_end AS LineEnd, n.is_external AS IsExternal, n.metadata_json AS Metadata
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteGraphReader(IDbConnectionFactory connectionFactory)
    {
        DapperBootstrap.EnsureTypeHandlersRegistered();
        _connectionFactory = connectionFactory;
    }

    public async Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<NodeDto>(
            $"SELECT {NodeColumns} FROM nodes WHERE node_id = @NodeId AND is_deleted = 0",
            new { NodeId = nodeId });
    }

    public async Task<IReadOnlyList<NodeDto>> FindByNameAsync(string name, NodeType? nodeType = null, bool exactMatch = false, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var sql = $"""
            SELECT {NodeColumns} FROM nodes
            WHERE is_deleted = 0
              AND (@ExactMatch = 1 AND name = @Name OR @ExactMatch = 0 AND name LIKE '%' || @Name || '%')
              AND (@NodeType IS NULL OR node_type = @NodeType)
            ORDER BY name
            """;

        var results = await connection.QueryAsync<NodeDto>(sql, new
        {
            Name = name,
            ExactMatch = exactMatch ? 1 : 0,
            NodeType = nodeType?.ToString(),
        });

        return results.ToList();
    }

    public async Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(string repoId = "default", CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var results = await connection.QueryAsync<ProjectDto>(
            """
            SELECT project_id AS ProjectId, repo_id AS RepoId, name AS Name, path AS Path,
                   target_framework AS TargetFramework, project_type AS ProjectType, layer AS Layer
            FROM projects
            WHERE repo_id = @RepoId
            ORDER BY name
            """,
            new { RepoId = repoId });

        return results.ToList();
    }

    public async Task<IReadOnlyList<NodeDto>> GetNodesByProjectAsync(string projectId, NodeType? nodeType = null, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var results = await connection.QueryAsync<NodeDto>(
            $"""
            SELECT {NodeColumns} FROM nodes
            WHERE project_id = @ProjectId AND is_deleted = 0
              AND (@NodeType IS NULL OR node_type = @NodeType)
            ORDER BY full_name
            """,
            new { ProjectId = projectId, NodeType = nodeType?.ToString() });

        return results.ToList();
    }

    public async Task<IReadOnlyList<EdgeWithNodeDto>> GetDependenciesAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var sql = $"""
            SELECT {EdgeColumnsJoined}, {JoinedNodeColumns} FROM edges e
            JOIN nodes n ON n.node_id = e.target_id
            WHERE e.source_id = @NodeId AND e.is_deleted = 0 AND n.is_deleted = 0
              AND (@RelationshipType IS NULL OR e.relationship_type = @RelationshipType)
            """;

        var rows = await connection.QueryAsync<EdgeDto, NodeDto, EdgeWithNodeDto>(
            sql,
            (edge, node) => new EdgeWithNodeDto { Edge = edge, OtherNode = node },
            new { NodeId = nodeId, RelationshipType = relationshipType?.ToString() },
            splitOn: "NodeId");

        return rows.ToList();
    }

    public async Task<IReadOnlyList<EdgeWithNodeDto>> GetCallersAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var sql = $"""
            SELECT {EdgeColumnsJoined}, {JoinedNodeColumns} FROM edges e
            JOIN nodes n ON n.node_id = e.source_id
            WHERE e.target_id = @NodeId AND e.is_deleted = 0 AND n.is_deleted = 0
              AND (@RelationshipType IS NULL OR e.relationship_type = @RelationshipType)
            """;

        var rows = await connection.QueryAsync<EdgeDto, NodeDto, EdgeWithNodeDto>(
            sql,
            (edge, node) => new EdgeWithNodeDto { Edge = edge, OtherNode = node },
            new { NodeId = nodeId, RelationshipType = relationshipType?.ToString() },
            splitOn: "NodeId");

        return rows.ToList();
    }
}

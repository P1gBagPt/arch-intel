using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Exceptions;
using ArchIntel.GraphStore.Core;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite;

public sealed class SqliteGraphWriter : IGraphWriter
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteGraphWriter(IDbConnectionFactory connectionFactory)
    {
        DapperBootstrap.EnsureTypeHandlersRegistered();
        _connectionFactory = connectionFactory;
    }

    public async Task<ScanHandle> BeginScanAsync(BeginScanRequest request, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var conflicting = await connection.QueryFirstOrDefaultAsync<long?>(
            "SELECT scan_run_id FROM scan_runs WHERE repo_id = @RepoId AND status = 'Running'",
            new { request.RepoId });

        if (conflicting is not null)
        {
            throw new ScanConflictException(request.RepoId);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var scanRunId = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO scan_runs (repo_id, started_at, scan_type, triggered_by, changed_files_json, status)
            VALUES (@RepoId, @StartedAt, @ScanType, @TriggeredBy, @ChangedFilesJson, 'Running')
            RETURNING scan_run_id
            """,
            new
            {
                request.RepoId,
                StartedAt = now,
                ScanType = request.ScanType.ToString(),
                request.TriggeredBy,
                ChangedFilesJson = request.ChangedFiles is null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(request.ChangedFiles),
            });

        return new ScanHandle
        {
            ScanRunId = scanRunId,
            RepoId = request.RepoId,
            ScanType = request.ScanType,
        };
    }

    public async Task UpsertProjectAsync(ScanHandle scan, ProjectDto project, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await UpsertProjectsAsync(connection, scan, [project]);
    }

    public async Task UpsertNodeAsync(ScanHandle scan, NodeDto node, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await UpsertNodesAsync(connection, scan, [node]);
    }

    public async Task UpsertNodesAsync(ScanHandle scan, IReadOnlyCollection<NodeDto> nodes, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await UpsertNodesAsync(connection, scan, nodes);
    }

    public async Task UpsertEdgeAsync(ScanHandle scan, EdgeDto edge, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await UpsertEdgesAsync(connection, scan, [edge]);
    }

    public async Task UpsertEdgesAsync(ScanHandle scan, IReadOnlyCollection<EdgeDto> edges, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        await UpsertEdgesAsync(connection, scan, edges);
    }

    public async Task CompleteScanAsync(ScanHandle scan, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow.ToString("O");
        await connection.ExecuteAsync(
            "UPDATE scan_runs SET status = 'Completed', completed_at = @Now WHERE scan_run_id = @ScanRunId",
            new { Now = now, scan.ScanRunId }, transaction);

        if (scan.ScanType == ScanType.Full)
        {
            // Full scan is authoritative: anything belonging to this repo not touched by this
            // scan run is stale and is removed (hard-deleted in Phase 1; soft-deleted from Phase 3).
            await connection.ExecuteAsync(
                """
                DELETE FROM edges
                WHERE repo_id = @RepoId
                  AND (scan_version < @ScanRunId
                       OR source_id IN (SELECT node_id FROM nodes WHERE repo_id = @RepoId AND scan_version < @ScanRunId)
                       OR target_id IN (SELECT node_id FROM nodes WHERE repo_id = @RepoId AND scan_version < @ScanRunId))
                """,
                new { scan.RepoId, scan.ScanRunId }, transaction);

            await connection.ExecuteAsync(
                "DELETE FROM nodes WHERE repo_id = @RepoId AND scan_version < @ScanRunId",
                new { scan.RepoId, scan.ScanRunId }, transaction);

            await connection.ExecuteAsync(
                "DELETE FROM projects WHERE repo_id = @RepoId AND scan_version < @ScanRunId",
                new { scan.RepoId, scan.ScanRunId }, transaction);
        }

        transaction.Commit();
    }

    public async Task FailScanAsync(ScanHandle scan, string errorMessage, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        var now = DateTimeOffset.UtcNow.ToString("O");
        await connection.ExecuteAsync(
            "UPDATE scan_runs SET status = 'Failed', completed_at = @Now, error_message = @ErrorMessage WHERE scan_run_id = @ScanRunId",
            new { Now = now, ErrorMessage = errorMessage, scan.ScanRunId });
    }

    private static async Task UpsertProjectsAsync(System.Data.IDbConnection connection, ScanHandle scan, IReadOnlyCollection<ProjectDto> projects)
    {
        if (projects.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO projects (project_id, repo_id, name, path, target_framework, project_type, layer, created_at, updated_at, scan_version)
            VALUES (@ProjectId, @RepoId, @Name, @Path, @TargetFramework, @ProjectType, @Layer, @Now, @Now, @ScanRunId)
            ON CONFLICT(project_id) DO UPDATE SET
                name = excluded.name,
                path = excluded.path,
                target_framework = excluded.target_framework,
                project_type = excluded.project_type,
                layer = excluded.layer,
                updated_at = excluded.updated_at,
                scan_version = excluded.scan_version
            """,
            projects.Select(p => new
            {
                p.ProjectId,
                p.RepoId,
                p.Name,
                p.Path,
                p.TargetFramework,
                p.ProjectType,
                p.Layer,
                Now = now,
                scan.ScanRunId,
            }),
            transaction);

        transaction.Commit();
    }

    private static async Task UpsertNodesAsync(System.Data.IDbConnection connection, ScanHandle scan, IReadOnlyCollection<NodeDto> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO nodes (node_id, repo_id, project_id, node_type, name, full_name, namespace, file_path, line_start, line_end, metadata_json, is_external, is_deleted, valid_from, valid_to, created_at, updated_at, scan_version)
            VALUES (@NodeId, @RepoId, @ProjectId, @NodeType, @Name, @FullName, @Namespace, @FilePath, @LineStart, @LineEnd, @Metadata, @IsExternal, 0, @Now, NULL, @Now, @Now, @ScanRunId)
            ON CONFLICT(node_id) DO UPDATE SET
                project_id = excluded.project_id,
                node_type = excluded.node_type,
                name = excluded.name,
                full_name = excluded.full_name,
                namespace = excluded.namespace,
                file_path = excluded.file_path,
                line_start = excluded.line_start,
                line_end = excluded.line_end,
                metadata_json = excluded.metadata_json,
                is_external = excluded.is_external,
                updated_at = excluded.updated_at,
                scan_version = excluded.scan_version
            """,
            nodes.Select(n => new
            {
                n.NodeId,
                n.RepoId,
                n.ProjectId,
                NodeType = n.NodeType.ToString(),
                n.Name,
                n.FullName,
                n.Namespace,
                n.FilePath,
                n.LineStart,
                n.LineEnd,
                Metadata = n.Metadata,
                IsExternal = n.IsExternal ? 1 : 0,
                Now = now,
                scan.ScanRunId,
            }),
            transaction);

        transaction.Commit();
    }

    private static async Task UpsertEdgesAsync(System.Data.IDbConnection connection, ScanHandle scan, IReadOnlyCollection<EdgeDto> edges)
    {
        if (edges.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO edges (edge_id, repo_id, source_id, target_id, relationship_type, metadata_json, is_deleted, valid_from, valid_to, created_at, updated_at, scan_version)
            VALUES (@EdgeId, @RepoId, @SourceId, @TargetId, @RelationshipType, @Metadata, 0, @Now, NULL, @Now, @Now, @ScanRunId)
            ON CONFLICT(edge_id) DO UPDATE SET
                source_id = excluded.source_id,
                target_id = excluded.target_id,
                relationship_type = excluded.relationship_type,
                metadata_json = excluded.metadata_json,
                updated_at = excluded.updated_at,
                scan_version = excluded.scan_version
            """,
            edges.Select(e => new
            {
                e.EdgeId,
                e.RepoId,
                e.SourceId,
                e.TargetId,
                RelationshipType = e.RelationshipType.ToString(),
                Metadata = e.Metadata,
                Now = now,
                scan.ScanRunId,
            }),
            transaction);

        transaction.Commit();
    }
}

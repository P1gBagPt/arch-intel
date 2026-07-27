using ArchIntel.Api.Contracts;
using ArchIntel.Api.Problems;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /repos/{repoId}/snapshots` and `.../snapshots/{id}/diff` (05-rest-api.md Section
/// 4.11). See SnapshotDto's doc comment — only the current scan is real; there is no historical
/// timeline to diff against yet.</summary>
public static class SnapshotsEndpoints
{
    public static IEndpointRouteBuilder MapSnapshotsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/snapshots", async (string repoId, IGraphReader reader, CancellationToken ct) =>
        {
            var snapshot = await ComputeCurrentSnapshotAsync(repoId, reader, ct);
            var data = snapshot is null ? Array.Empty<SnapshotDto>() : [snapshot];
            return Results.Ok(new ApiEnvelope<IReadOnlyList<SnapshotDto>>(data));
        })
        .WithName("GetSnapshots")
        .WithTags("Snapshots")
        .RequireAuthorization("RequireRepoViewer")
        .Produces<ApiEnvelope<IReadOnlyList<SnapshotDto>>>();

        app.MapGet("/snapshots/{id}", async (string repoId, string id, IGraphReader reader, CancellationToken ct) =>
        {
            var snapshot = await ComputeCurrentSnapshotAsync(repoId, reader, ct);
            if (snapshot is null || snapshot.SnapshotId != id)
            {
                return ProblemTypes.SnapshotNotFound(id, $"/snapshots/{id}");
            }

            return Results.Ok(new ApiEnvelope<SnapshotDto>(snapshot));
        })
        .WithName("GetSnapshot")
        .WithTags("Snapshots")
        .RequireAuthorization("RequireRepoViewer")
        .Produces<ApiEnvelope<SnapshotDto>>()
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/snapshots/{id}/diff", (string id) => ProblemTypes.SnapshotHistoryUnavailable($"/snapshots/{id}/diff"))
        .WithName("GetSnapshotDiff")
        .WithTags("Snapshots")
        .RequireAuthorization("RequireRepoViewer")
        .Produces(StatusCodes.Status501NotImplemented);

        return app;
    }

    private static async Task<SnapshotDto?> ComputeCurrentSnapshotAsync(string repoId, IGraphReader reader, CancellationToken ct)
    {
        var metadata = await reader.GetLatestScanMetadataAsync(repoId, ct);
        if (metadata is null)
        {
            return null;
        }

        var projects = await reader.ListProjectsAsync(repoId, ct);
        var classCount = 0;
        var interfaceCount = 0;
        foreach (var project in projects)
        {
            var nodes = await reader.GetNodesByProjectAsync(project.ProjectId, nodeType: null, ct: ct);
            classCount += nodes.Count(n => n.NodeType == NodeType.Class);
            interfaceCount += nodes.Count(n => n.NodeType == NodeType.Interface);
        }

        return new SnapshotDto($"snap_{metadata.ScanRunId}", metadata.CompletedAt, classCount, projects.Count, interfaceCount);
    }
}

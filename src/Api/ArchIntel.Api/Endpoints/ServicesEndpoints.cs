using ArchIntel.Api.Contracts;
using ArchIntel.Api.Mapping;
using ArchIntel.Api.Pagination;
using ArchIntel.Api.Problems;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /services` and `GET /services/{id}` (05-rest-api.md Sections 4.2/4.3).</summary>
public static class ServicesEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/services", async (string repoId, IGraphReader reader, string? cursor, int? limit, CancellationToken ct) =>
        {
            if (!CursorPagination.TryDecode(cursor, out var offset))
            {
                return ProblemTypes.InvalidQuery("Malformed cursor.");
            }

            var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

            var projects = await reader.ListProjectsAsync(repoId, ct);
            var all = new List<ServiceSummaryDto>();
            foreach (var project in projects)
            {
                var nodes = await reader.GetNodesByProjectAsync(project.ProjectId, nodeType: null, ct: ct);
                all.AddRange(nodes.Where(n => ServiceNodeTypes.Kinds.Contains(n.NodeType)).Select(n => n.ToServiceDto()));
            }

            var page = all.Skip(offset).Take(effectiveLimit).ToList();
            var hasNextPage = offset + effectiveLimit < all.Count;
            var pageInfo = new PageInfo(
                effectiveLimit,
                all.Count,
                hasNextPage,
                hasNextPage ? CursorPagination.Encode(offset + effectiveLimit) : null);

            return Results.Ok(new ApiEnvelope<IReadOnlyList<ServiceSummaryDto>>(page, pageInfo));
        })
        .WithName("GetServices")
        .WithTags("Services")
        .RequireAuthorization("RequireRepoViewer")
        .Produces<ApiEnvelope<IReadOnlyList<ServiceSummaryDto>>>()
        .ProducesValidationProblem();

        app.MapGet("/services/{id}", async (string id, IGraphReader reader, CancellationToken ct) =>
        {
            var node = await reader.GetNodeAsync(id, ct);
            if (node is null)
            {
                return ProblemTypes.NodeNotFound(id, $"/services/{id}");
            }

            var detail = await ComposeDetailAsync(reader, node, ct);
            return Results.Ok(new ApiEnvelope<ServiceDetailDto>(detail));
        })
        .WithName("GetServiceDetail")
        .WithTags("Services")
        .RequireAuthorization("RequireRepoViewer")
        .Produces<ApiEnvelope<ServiceDetailDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Composes the Service Explorer detail view purely from IGraphReader's 1-hop queries.
    /// "Implements" is read straight off the node's own outgoing Implements edges. "Tests" has no
    /// dedicated relationship in the schema — the scanner only emits a TestMethod --Calls--> Method
    /// edge (test -> tested) — so it's rebuilt from two relationship types that do exist: walk this
    /// node's Contains edges down to its own methods, find TestMethods that Call those methods, then
    /// walk each TestMethod's Contains edge back up to its containing TestClass.</summary>
    private static async Task<ServiceDetailDto> ComposeDetailAsync(IGraphReader reader, NodeDto node, CancellationToken ct)
    {
        var dependencies = await reader.GetDependenciesAsync(node.NodeId, ct: ct);
        var callers = await reader.GetCallersAsync(node.NodeId, ct: ct);

        var implements = dependencies
            .Where(d => d.Edge.RelationshipType == RelationshipType.Implements)
            .Select(d => d.OtherNode.ToNodeRefDto())
            .ToList();

        var methods = dependencies
            .Where(d => d.Edge.RelationshipType == RelationshipType.Contains)
            .Select(d => d.OtherNode)
            .ToList();

        var testClassIds = new HashSet<string>();
        var tests = new List<NodeRefDto>();
        foreach (var method in methods)
        {
            var methodCallers = await reader.GetCallersAsync(method.NodeId, RelationshipType.Calls, ct);
            foreach (var testMethod in methodCallers.Where(c => c.OtherNode.NodeType == NodeType.TestMethod).Select(c => c.OtherNode))
            {
                var containers = await reader.GetCallersAsync(testMethod.NodeId, RelationshipType.Contains, ct);
                foreach (var testClass in containers.Where(c => c.OtherNode.NodeType == NodeType.TestClass).Select(c => c.OtherNode))
                {
                    if (testClassIds.Add(testClass.NodeId))
                    {
                        tests.Add(testClass.ToNodeRefDto());
                    }
                }
            }
        }

        var realDependencies = dependencies
            .Where(d => d.Edge.RelationshipType is not (RelationshipType.Contains or RelationshipType.Implements))
            .Select(d => d.ToNodeRefDto())
            .ToList();

        // Exclude Contains from callers too — an incoming Contains edge is just "my namespace/class
        // declares me", structural noise rather than a real caller/dependent.
        var realCallers = callers
            .Where(c => c.Edge.RelationshipType != RelationshipType.Contains)
            .Select(c => c.ToNodeRefDto())
            .ToList();

        return new ServiceDetailDto(
            node.NodeId,
            node.Name,
            node.NodeType.ToString(),
            node.ProjectId,
            realDependencies,
            realCallers,
            implements,
            tests);
    }
}

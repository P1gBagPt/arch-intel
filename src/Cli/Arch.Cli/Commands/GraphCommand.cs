using System.CommandLine;
using Arch.Cli.Configuration;
using Arch.Cli.Output;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Sqlite;

namespace Arch.Cli.Commands;

/// <summary>
/// `arch graph` — query/render the dependency graph (03-cli.md Section 4, "arch graph").
/// The `[node]` neighborhood view does a real multi-hop expansion via a BFS layered on top of
/// IGraphReader's 1-hop GetDependenciesAsync/GetCallersAsync (see ExpandTreeAsync) rather than the
/// Graph Store's GetNeighborhoodAsync/GetImpactAsync — those return a flat node list with no
/// per-node relationship/depth, which would lose exactly the "Calls → X" labeling this tree wants
/// at every level, not just the first one.
/// </summary>
public static class GraphCommand
{
    private const int MaxDepthOption = 5;

    public static Command Build()
    {
        var nodeArgument = new Argument<string?>("node") { Description = "Scope the graph to this node's neighborhood", Arity = ArgumentArity.ZeroOrOne };
        var projectOption = new Option<string?>("--project") { Description = "Filter to a project" };
        var typeOption = new Option<string?>("--type") { Description = "Filter by node kind (e.g. Class, Interface, Service, Controller)" };
        var depthOption = new Option<int>("--depth") { Description = "Traversal depth from [node] (1-5)", DefaultValueFactory = _ => 2 };
        var excludeTestsOption = new Option<bool>("--exclude-tests") { Description = "Omit test projects/classes" };

        var command = new Command("graph", "Query and render the dependency graph, in full or scoped to a node.")
        {
            nodeArgument, projectOption, typeOption, depthOption, excludeTestsOption,
        };

        command.SetAction((parseResult, ct) => RunAsync(
            parseResult.GetValue(GlobalOptions.Config),
            parseResult.GetValue(GlobalOptions.Cwd)!,
            parseResult.GetValue(nodeArgument),
            parseResult.GetValue(projectOption),
            parseResult.GetValue(typeOption),
            parseResult.GetValue(depthOption),
            parseResult.GetValue(excludeTestsOption),
            OutputWriterFactory.Create(parseResult),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        string? configPathOption,
        string cwd,
        string? node,
        string? project,
        string? typeFilter,
        int depth,
        bool excludeTests,
        IOutputWriter output,
        CancellationToken ct = default)
    {
        ResolvedConfig resolved;
        try
        {
            resolved = ConfigDiscovery.Load(configPathOption, cwd);
        }
        catch (Exception ex) when (ex is ConfigNotFoundException or FileNotFoundException)
        {
            output.WriteError(ex.Message);
            return ExitCodes.ConfigurationError;
        }

        NodeType? nodeTypeFilter = null;
        if (typeFilter is not null)
        {
            if (!Enum.TryParse<NodeType>(typeFilter, ignoreCase: true, out var parsed))
            {
                output.WriteError($"Unknown node type '{typeFilter}'. Valid values: {string.Join(", ", Enum.GetNames<NodeType>())}");
                return ExitCodes.UserError;
            }

            nodeTypeFilter = parsed;
        }

        var configDir = Path.GetDirectoryName(resolved.Path)!;
        var dbPath = GraphStorePaths.ResolveDbPath(resolved.Config, configDir);
        if (!File.Exists(dbPath))
        {
            output.WriteError($"Graph database not found at {dbPath}. Run 'arch scan' first.");
            return ExitCodes.EnvironmentError;
        }

        var reader = new SqliteGraphReader(new SqliteConnectionFactory($"Data Source={dbPath}"));

        if (node is not null)
        {
            return await RunNodeScopedAsync(reader, node, nodeTypeFilter, Math.Clamp(depth, 1, MaxDepthOption), output, ct);
        }

        if (project is not null)
        {
            return await RunProjectScopedAsync(reader, project, nodeTypeFilter, excludeTests, output, ct);
        }

        var projects = await reader.ListProjectsAsync(ct: ct);
        output.WriteTable(new TableData(
            ["Name", "Path", "Type", "Layer"],
            projects.Select(p => (IReadOnlyList<string>)[p.Name, p.Path, p.ProjectType ?? "", p.Layer ?? ""]).ToList()));

        return ExitCodes.Success;
    }

    private static async Task<int> RunNodeScopedAsync(
        SqliteGraphReader reader, string name, NodeType? nodeTypeFilter, int depth, IOutputWriter output, CancellationToken ct)
    {
        var matches = await reader.FindByNameAsync(name, nodeTypeFilter, exactMatch: false, ct: ct);
        if (matches.Count == 0)
        {
            output.WriteError($"No node found matching '{name}'.");
            return ExitCodes.UserError;
        }

        if (matches.Count > 1)
        {
            output.WriteError($"Multiple nodes match '{name}': {string.Join(", ", matches.Select(m => m.FullName))}. Be more specific.");
            return ExitCodes.UserError;
        }

        var target = matches[0];
        var budget = new TraversalBudget();
        var dependsOn = await ExpandTreeAsync(reader, target.NodeId, depth, forward: true, budget, ct);
        var usedBy = await ExpandTreeAsync(reader, target.NodeId, depth, forward: false, budget, ct);

        var tree = new TreeNodeData($"{target.Name} ({target.FullName})",
        [
            new TreeNodeData("Depends on", dependsOn),
            new TreeNodeData("Used by", usedBy),
        ]);

        output.WriteTree(tree);
        if (budget.Truncated)
        {
            output.WriteRaw($"(note: stopped after {TraversalBudget.MaxNodes} nodes; narrow with --type or a smaller --depth to see more)");
        }

        return ExitCodes.Success;
    }

    /// <summary>Recursively expands a node's dependencies (forward) or callers (reverse) up to
    /// `remainingDepth` hops. `ancestors` tracks only the current root-to-node path (not a global
    /// visited set — added before recursing, removed after returning) so a node reachable via two
    /// different branches still appears under both; a node that's already an ancestor on the current
    /// path renders as a "(cycle)" leaf instead of recursing forever.</summary>
    private static async Task<List<TreeNodeData>> ExpandTreeAsync(
        SqliteGraphReader reader, string nodeId, int remainingDepth, bool forward, TraversalBudget budget, CancellationToken ct, HashSet<string>? ancestors = null)
    {
        ancestors ??= [nodeId];
        if (remainingDepth <= 0 || budget.Truncated)
        {
            return [];
        }

        var edges = forward
            ? await reader.GetDependenciesAsync(nodeId, ct: ct)
            : await reader.GetCallersAsync(nodeId, ct: ct);

        var arrow = forward ? "→" : "←";
        var children = new List<TreeNodeData>();
        foreach (var edge in edges)
        {
            if (budget.Truncated)
            {
                break;
            }

            var otherNode = edge.OtherNode;
            var label = $"{edge.Edge.RelationshipType} {arrow} {otherNode.Name}";

            if (ancestors.Contains(otherNode.NodeId))
            {
                children.Add(new TreeNodeData($"{label} (cycle)"));
                continue;
            }

            if (!budget.TryConsume())
            {
                break;
            }

            ancestors.Add(otherNode.NodeId);
            var grandchildren = await ExpandTreeAsync(reader, otherNode.NodeId, remainingDepth - 1, forward, budget, ct, ancestors);
            ancestors.Remove(otherNode.NodeId);
            children.Add(new TreeNodeData(label, grandchildren));
        }

        return children;
    }

    private sealed class TraversalBudget
    {
        public const int MaxNodes = 300;
        private int _remaining = MaxNodes;

        public bool Truncated { get; private set; }

        public bool TryConsume()
        {
            if (_remaining <= 0)
            {
                Truncated = true;
                return false;
            }

            _remaining--;
            return true;
        }
    }

    private static async Task<int> RunProjectScopedAsync(
        SqliteGraphReader reader, string projectName, NodeType? nodeTypeFilter, bool excludeTests, IOutputWriter output, CancellationToken ct)
    {
        var projects = await reader.ListProjectsAsync(ct: ct);
        // Exact match first (project names are the real .csproj name, e.g. "SampleErp.Application"),
        // falling back to a substring match so `--project Application` also works without the prefix.
        var project = projects.FirstOrDefault(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            var substringMatches = projects.Where(p => p.Name.Contains(projectName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (substringMatches.Count > 1)
            {
                output.WriteError($"Multiple projects match '{projectName}': {string.Join(", ", substringMatches.Select(p => p.Name))}. Be more specific.");
                return ExitCodes.UserError;
            }

            project = substringMatches.SingleOrDefault();
        }

        if (project is null)
        {
            output.WriteError($"No project named '{projectName}'. Known projects: {string.Join(", ", projects.Select(p => p.Name))}");
            return ExitCodes.UserError;
        }

        var nodes = await reader.GetNodesByProjectAsync(project.ProjectId, nodeTypeFilter, ct);
        if (excludeTests)
        {
            nodes = nodes.Where(n => n.NodeType is not (NodeType.TestClass or NodeType.TestMethod)).ToList();
        }

        var children = new List<TreeNodeData>();
        foreach (var n in nodes)
        {
            var dependencies = await reader.GetDependenciesAsync(n.NodeId, ct: ct);
            children.Add(new TreeNodeData(
                $"{n.NodeType} {n.Name}",
                dependencies.Select(d => new TreeNodeData($"{d.Edge.RelationshipType} → {d.OtherNode.Name}")).ToList()));
        }

        output.WriteTree(new TreeNodeData($"{project.Name} (project)", children));
        return ExitCodes.Success;
    }
}

using System.Collections.ObjectModel;

namespace DP.WorkFlow;

/// <summary>
/// Builds one immutable, indexed interpretation of a workflow control graph for compilation and binding analysis.
/// </summary>
/// <remarks>
/// This module deliberately ignores canvas layout. Callers no longer need to rebuild outgoing, incoming,
/// reachability, and dominance structures independently.
/// </remarks>
internal sealed class WorkflowGraphIndex
{
    private readonly IReadOnlyDictionary<string, IWorkflowNodeModel> _nodes;
    private readonly IReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>> _outgoingByPort;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _successors;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _predecessors;

    private WorkflowGraphIndex(
        IReadOnlyDictionary<string, IWorkflowNodeModel> nodes,
        IReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>> outgoingByPort,
        IReadOnlyDictionary<string, IReadOnlyList<string>> successors,
        IReadOnlyDictionary<string, IReadOnlyList<string>> predecessors)
    {
        _nodes = nodes;
        _outgoingByPort = outgoingByPort;
        _successors = successors;
        _predecessors = predecessors;
    }

    /// <summary>Gets the first node configuration for each non-empty, ordinal node ID.</summary>
    public IReadOnlyDictionary<string, IWorkflowNodeModel> Nodes => _nodes;

    /// <summary>Gets the control transitions indexed by source node and outcome port.</summary>
    public IReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>> OutgoingByPort => _outgoingByPort;

    /// <summary>Gets successors collapsed across all source outcome ports.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Successors => _successors;

    /// <summary>Gets direct predecessors indexed by target node.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Predecessors => _predecessors;

    /// <summary>Creates an indexed semantic projection without retaining connection or layout objects.</summary>
    /// <param name="graph">The document control graph to project.</param>
    /// <returns>An immutable control-graph index.</returns>
    public static WorkflowGraphIndex Create(WorkflowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes
            .Where(node => node is not null && !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var connections = graph.ControlConnections
            .Where(connection => connection.State == WorkflowConnectionState.Active)
            .ToArray();

        var outgoingByPort = connections
            .Where(connection => !string.IsNullOrWhiteSpace(connection.FromNodeId)
                && !string.IsNullOrWhiteSpace(connection.FromPort))
            .GroupBy(connection => new WorkflowPortAddress(connection.FromNodeId, connection.FromPort))
            .ToDictionary(
                group => group.Key,
                group => AsReadOnlyDistinct(group.Select(connection => connection.ToNodeId)));

        var successors = nodes.Keys.ToDictionary(
            nodeId => nodeId,
            nodeId => AsReadOnlyDistinct(connections
                .Where(connection => string.Equals(connection.FromNodeId, nodeId, StringComparison.Ordinal))
                .Select(connection => connection.ToNodeId)),
            StringComparer.Ordinal);
        var predecessors = nodes.Keys.ToDictionary(
            nodeId => nodeId,
            nodeId => AsReadOnlyDistinct(connections
                .Where(connection => string.Equals(connection.ToNodeId, nodeId, StringComparison.Ordinal))
                .Select(connection => connection.FromNodeId)),
            StringComparer.Ordinal);

        return new WorkflowGraphIndex(
            new ReadOnlyDictionary<string, IWorkflowNodeModel>(nodes),
            new ReadOnlyDictionary<WorkflowPortAddress, IReadOnlyList<string>>(outgoingByPort),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(successors),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(predecessors));
    }

    /// <summary>Gets all distinct successors selected through one control outcome.</summary>
    /// <param name="nodeId">The source node ID.</param>
    /// <param name="portKey">The source outcome port key.</param>
    /// <returns>Targets in document connection order.</returns>
    public IReadOnlyList<string> GetSuccessors(string nodeId, string portKey) =>
        _outgoingByPort.TryGetValue(new WorkflowPortAddress(nodeId, portKey), out var targets)
            ? targets
            : Array.Empty<string>();

    /// <summary>Gets all distinct successors across every outcome port.</summary>
    /// <param name="nodeId">The source node ID.</param>
    /// <returns>Targets in document connection order.</returns>
    public IReadOnlyList<string> GetSuccessors(string nodeId) =>
        _successors.TryGetValue(nodeId, out var targets) ? targets : Array.Empty<string>();

    /// <summary>Gets all distinct direct predecessors.</summary>
    /// <param name="nodeId">The target node ID.</param>
    /// <returns>Sources in document connection order.</returns>
    public IReadOnlyList<string> GetPredecessors(string nodeId) =>
        _predecessors.TryGetValue(nodeId, out var sources) ? sources : Array.Empty<string>();

    /// <summary>Collects all nodes reachable from a start node, including the start node.</summary>
    /// <param name="startNodeId">The traversal start.</param>
    /// <param name="stopBeforeNodeId">An optional boundary that is included but never traversed beyond.</param>
    /// <returns>An ordinal set of reachable node IDs.</returns>
    public HashSet<string> CollectReachable(string startNodeId, string? stopBeforeNodeId = null)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(startNodeId);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!reachable.Add(current)
                || string.Equals(current, stopBeforeNodeId, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var target in GetSuccessors(current))
                pending.Push(target);
        }
        return reachable;
    }

    /// <summary>Computes the shortest number of control transitions between two nodes.</summary>
    /// <param name="startNodeId">The traversal start.</param>
    /// <param name="targetNodeId">The target node.</param>
    /// <returns>The shortest distance, or -1 when the target is unreachable.</returns>
    public int ComputeShortestDistance(string startNodeId, string targetNodeId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string NodeId, int Distance)>();
        pending.Enqueue((startNodeId, 0));
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.NodeId))
                continue;
            if (string.Equals(current.NodeId, targetNodeId, StringComparison.Ordinal))
                return current.Distance;
            foreach (var target in GetSuccessors(current.NodeId))
                pending.Enqueue((target, current.Distance + 1));
        }
        return -1;
    }

    /// <summary>Computes the dominator set for every node reachable from the entry.</summary>
    /// <param name="startNodeId">The control-graph entry.</param>
    /// <returns>Each reachable node and the nodes that occur on every path to it.</returns>
    public IReadOnlyDictionary<string, HashSet<string>> ComputeDominators(string startNodeId)
    {
        var reachable = CollectReachable(startNodeId);
        var result = reachable.ToDictionary(
            nodeId => nodeId,
            nodeId => nodeId == startNodeId
                ? new HashSet<string>(new[] { startNodeId }, StringComparer.Ordinal)
                : new HashSet<string>(reachable, StringComparer.Ordinal),
            StringComparer.Ordinal);

        bool changed;
        do
        {
            changed = false;
            foreach (var nodeId in reachable.Where(id => id != startNodeId))
            {
                var incoming = GetPredecessors(nodeId).Where(reachable.Contains).ToArray();
                var next = incoming.Length == 0
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(result[incoming[0]], StringComparer.Ordinal);
                foreach (var predecessor in incoming.Skip(1))
                    next.IntersectWith(result[predecessor]);
                next.Add(nodeId);
                if (next.SetEquals(result[nodeId]))
                    continue;
                result[nodeId] = next;
                changed = true;
            }
        } while (changed);

        return new ReadOnlyDictionary<string, HashSet<string>>(result);
    }

    /// <summary>Computes post-dominator sets for nodes reachable from the supplied entries.</summary>
    /// <param name="entryNodeIds">One or more control-flow entries.</param>
    /// <returns>Each reachable node and the nodes occurring on every finite path from it to a terminal.</returns>
    public IReadOnlyDictionary<string, HashSet<string>> ComputePostDominators(IEnumerable<string> entryNodeIds)
    {
        ArgumentNullException.ThrowIfNull(entryNodeIds);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entryNodeId in entryNodeIds)
            reachable.UnionWith(CollectReachable(entryNodeId));
        var exits = reachable.Where(nodeId => GetSuccessors(nodeId).All(target => !reachable.Contains(target))).ToArray();
        if (exits.Length == 0)
            return new ReadOnlyDictionary<string, HashSet<string>>(
                reachable.ToDictionary(nodeId => nodeId, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal));

        var result = reachable.ToDictionary(
            nodeId => nodeId,
            nodeId => exits.Contains(nodeId, StringComparer.Ordinal)
                ? new HashSet<string>(new[] { nodeId }, StringComparer.Ordinal)
                : new HashSet<string>(reachable, StringComparer.Ordinal),
            StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var nodeId in reachable.Where(id => !exits.Contains(id, StringComparer.Ordinal)))
            {
                var successors = GetSuccessors(nodeId).Where(reachable.Contains).ToArray();
                if (successors.Length == 0)
                    continue;
                var next = new HashSet<string>(result[successors[0]], StringComparer.Ordinal);
                foreach (var successor in successors.Skip(1))
                    next.IntersectWith(result[successor]);
                next.Add(nodeId);
                if (next.SetEquals(result[nodeId]))
                    continue;
                result[nodeId] = next;
                changed = true;
            }
        } while (changed);
        return new ReadOnlyDictionary<string, HashSet<string>>(result);
    }

    private static IReadOnlyList<string> AsReadOnlyDistinct(IEnumerable<string> values) =>
        Array.AsReadOnly(values.Distinct(StringComparer.Ordinal).ToArray());
}

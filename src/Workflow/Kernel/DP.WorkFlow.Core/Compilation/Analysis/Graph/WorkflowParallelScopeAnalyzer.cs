using System.Collections.ObjectModel;

namespace DP.WorkFlow;

/// <summary>
/// Derives structured parallel scopes from one indexed control graph and rejects overlapping branch bodies.
/// </summary>
internal static class WorkflowParallelScopeAnalyzer
{
    /// <summary>Finds every valid ParallelAll/WaitAll scope and appends blocking structural diagnostics.</summary>
    /// <param name="graph">The indexed control graph.</param>
    /// <param name="diagnostics">The destination for structural errors.</param>
    /// <returns>Valid scopes indexed by their dispatch node ID.</returns>
    public static IReadOnlyDictionary<string, WorkflowParallelScopePlan> Analyze(
        WorkflowGraphIndex graph,
        ICollection<WorkflowValidationError> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var scopes = new Dictionary<string, WorkflowParallelScopePlan>(StringComparer.Ordinal);
        foreach (var parallelNode in graph.Nodes.Values.OfType<IWorkflowParallelForkNode>())
        {
            var branches = graph.GetSuccessors(parallelNode.Id, WorkflowPorts.Branch);
            // 单分支也允许（例如其余分支暂时停用或尚未接入）；停用的分支不派发，启用的至少一个。
            int disabledBranches = graph.DisabledConnections.Count(c => c.FromNodeId == parallelNode.Id && c.FromPort == WorkflowPorts.Branch);
            if (branches.Count + disabledBranches == 0)
            {
                diagnostics.Add(new WorkflowValidationError(
                    "WF020",
                    $"并行节点 {parallelNode.Id} 没有连出任何 Branch 分支。",
                    parallelNode.Id));
                continue;
            }
            if (branches.Count == 0)
            {
                diagnostics.Add(new WorkflowValidationError(
                    "WF023",
                    $"并行节点 {parallelNode.Id} 的分支已全部停用，至少保留一个启用的分支。",
                    parallelNode.Id));
                continue;
            }

            HashSet<string>? common = null;
            foreach (var branch in branches)
            {
                var reachable = graph.CollectReachable(branch);
                if (common is null)
                    common = reachable;
                else
                    common.IntersectWith(reachable);
            }

            // 回到并行节点的边（流程首尾回环）视为本次并行的终点，汇聚点仍按分支到这些终点的路径判定。
            var postDominators = graph.ComputePostDominators(branches, parallelNode.Id);
            var candidates = (common ?? new HashSet<string>(StringComparer.Ordinal))
                .Where(nodeId => graph.Nodes.TryGetValue(nodeId, out var node)
                    && node is IWorkflowParallelJoinNode
                    && branches.All(branch => postDominators.TryGetValue(branch, out var set) && set.Contains(nodeId)))
                .Select(nodeId => new
                {
                    NodeId = nodeId,
                    Score = branches.Max(branch => graph.ComputeShortestDistance(branch, nodeId))
                })
                .Where(item => item.Score >= 0)
                .OrderBy(item => item.Score)
                .ThenBy(item => item.NodeId, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                diagnostics.Add(new WorkflowValidationError(
                    "WF021",
                    $"并行节点 {parallelNode.Id} 的分支没有共同且后支配全部分支的 WaitAllInputsCompleted 汇聚节点。",
                    parallelNode.Id));
                continue;
            }

            var mergeNodeId = candidates[0].NodeId;
            if (TryFindOverlap(graph, branches, mergeNodeId, out var overlapNodeId))
            {
                diagnostics.Add(new WorkflowValidationError(
                    "WF022",
                    $"并行节点 {parallelNode.Id} 的分支在汇聚节点 {mergeNodeId} 之前共享节点 {overlapNodeId}。并行分支必须在显式汇聚前保持互斥。",
                    parallelNode.Id));
                continue;
            }

            scopes.Add(parallelNode.Id, new WorkflowParallelScopePlan(
                parallelNode.Id,
                Array.AsReadOnly(branches.ToArray()),
                mergeNodeId));
        }

        return new ReadOnlyDictionary<string, WorkflowParallelScopePlan>(scopes);
    }

    private static bool TryFindOverlap(
        WorkflowGraphIndex graph,
        IReadOnlyList<string> branches,
        string mergeNodeId,
        out string? overlapNodeId)
    {
        var ownerByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var branch in branches)
        {
            var branchBody = graph.CollectReachable(branch, mergeNodeId);
            branchBody.Remove(mergeNodeId);
            foreach (var nodeId in branchBody.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (ownerByNode.TryGetValue(nodeId, out var owner)
                    && !string.Equals(owner, branch, StringComparison.Ordinal))
                {
                    overlapNodeId = nodeId;
                    return true;
                }
                ownerByNode[nodeId] = branch;
            }
        }

        overlapNodeId = null;
        return false;
    }
}

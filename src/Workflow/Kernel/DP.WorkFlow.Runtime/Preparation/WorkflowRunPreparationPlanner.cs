namespace DP.WorkFlow;

/// <summary>把编译计划转换为有稳定路径的准备节点，不使用运行Token代替计划位置。</summary>
public static class WorkflowRunPreparationPlanner
{
    /// <summary>递归展开根计划和所有子计划。</summary>
    public static IReadOnlyList<WorkflowPreparationNode> Enumerate(WorkflowExecutionPlan plan, string planPath = "$")
    {
        var nodes = new List<WorkflowPreparationNode>();
        foreach (var nodeId in plan.NodeIds) nodes.Add(new WorkflowPreparationNode(planPath, plan.GetNodeOrThrow(nodeId)));
        foreach (var child in plan.ChildPlans.OrderBy(p => p.Key, StringComparer.Ordinal))
            nodes.AddRange(Enumerate(child.Value, ChildPath(planPath, child.Key)));
        return nodes.AsReadOnly();
    }
    /// <summary>编码子计划路径段，节点Id中的斜杠不能改变层级。</summary>
    public static string ChildPath(string parentPath, string nodeId) => parentPath + "/" + Uri.EscapeDataString(nodeId);
}

namespace DP.WorkFlow;

/// <summary>描述一个冻结执行计划中缺失的宿主运行能力。</summary>
/// <param name="PlanPath">根计划到当前子计划的稳定节点路径。</param>
/// <param name="NodeId">要求该能力的节点标识。</param>
/// <param name="NodeType">要求该能力的节点类型。</param>
/// <param name="CapabilityType">宿主缺失的能力类型。</param>
/// <param name="Description">能力用途的可选说明。</param>
public sealed record WorkflowRuntimeCapabilityIssue(
    string PlanPath,
    string NodeId,
    string NodeType,
    Type CapabilityType,
    string? Description);

/// <summary>表示工作流开始前发现一个或多个宿主运行能力缺失。</summary>
public sealed class WorkflowRuntimeCapabilityException : InvalidOperationException
{
    /// <summary>创建包含全部缺失能力事实的异常。</summary>
    /// <param name="issues">按计划路径、节点和能力类型稳定排序的问题。</param>
    public WorkflowRuntimeCapabilityException(IReadOnlyList<WorkflowRuntimeCapabilityIssue> issues)
        : base(CreateMessage(issues))
    {
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    /// <summary>获取本次运行准备阶段发现的全部缺失能力。</summary>
    public IReadOnlyList<WorkflowRuntimeCapabilityIssue> Issues { get; }

    private static string CreateMessage(IReadOnlyList<WorkflowRuntimeCapabilityIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0)
            return "工作流运行能力预检失败，但没有提供问题详情。";
        var details = issues.Select(issue =>
            $"{issue.PlanPath}/{issue.NodeId} ({issue.NodeType}) 缺少 {issue.CapabilityType.FullName}"
            + (issue.Description is null ? string.Empty : $"：{issue.Description}"));
        return $"工作流运行能力预检失败：{Environment.NewLine}{string.Join(Environment.NewLine, details)}";
    }
}

/// <summary>递归分析冻结计划及其子计划，并在节点执行前验证宿主运行能力。</summary>
public static class WorkflowRuntimeCapabilityValidator
{
    /// <summary>返回根计划和全部子计划中缺失的运行能力。</summary>
    /// <param name="plan">已经绑定 Handler 和能力要求的执行计划。</param>
    /// <param name="capabilities">本次运行使用的宿主能力容器。</param>
    /// <returns>确定性排序且不会修改计划的缺失能力集合。</returns>
    public static IReadOnlyList<WorkflowRuntimeCapabilityIssue> Analyze(
        WorkflowBoundExecutionPlan plan,
        IServiceProvider capabilities)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(capabilities);
        var availability = new Dictionary<Type, bool>();
        var issues = new List<WorkflowRuntimeCapabilityIssue>();
        AnalyzePlan(plan, "$", capabilities, availability, issues);
        return issues
            .OrderBy(issue => issue.PlanPath, StringComparer.Ordinal)
            .ThenBy(issue => issue.NodeId, StringComparer.Ordinal)
            .ThenBy(issue => issue.CapabilityType.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>验证全部运行能力；存在缺失项时一次抛出包含完整问题集合的异常。</summary>
    /// <param name="plan">已经绑定 Handler 和能力要求的执行计划。</param>
    /// <param name="capabilities">本次运行使用的宿主能力容器。</param>
    /// <exception cref="WorkflowRuntimeCapabilityException">至少一个计划节点缺少必需能力。</exception>
    public static void Validate(WorkflowBoundExecutionPlan plan, IServiceProvider capabilities)
    {
        var issues = Analyze(plan, capabilities);
        if (issues.Count > 0)
            throw new WorkflowRuntimeCapabilityException(issues);
    }

    private static void AnalyzePlan(
        WorkflowBoundExecutionPlan plan,
        string planPath,
        IServiceProvider capabilities,
        IDictionary<Type, bool> availability,
        ICollection<WorkflowRuntimeCapabilityIssue> issues)
    {
        foreach (var nodeId in plan.Plan.NodeIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            var node = plan.Plan.GetNodeOrThrow(nodeId);
            foreach (var requirement in plan.GetRequiredCapabilities(nodeId))
            {
                if (!availability.TryGetValue(requirement.CapabilityType, out var available))
                {
                    available = capabilities.GetService(requirement.CapabilityType) is not null;
                    availability.Add(requirement.CapabilityType, available);
                }
                if (!available)
                {
                    issues.Add(new WorkflowRuntimeCapabilityIssue(
                        planPath,
                        node.Id,
                        node.NodeType,
                        requirement.CapabilityType,
                        requirement.Description));
                }
            }
        }

        foreach (var child in plan.Plan.ChildPlans.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            AnalyzePlan(plan.GetChildPlan(child.Key), $"{planPath}/{child.Key}", capabilities, availability, issues);
    }
}

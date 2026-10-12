namespace DP.WorkFlow;

/// <summary>复合节点运行时读取子流程接口：解析传入的输入值，取回子画布的输出值。</summary>
internal static class WorkflowSubflowRuntime
{
    /// <summary>在父流程中解析各项输入：绑定来源读取父画布节点输出，否则使用手动值。</summary>
    /// <param name="node">复合节点。</param>
    /// <param name="context">复合节点本次执行的上下文。</param>
    /// <returns>按输入名称索引的值。</returns>
    internal static IReadOnlyDictionary<string, object?> ResolveInputs(IWorkflowSubflowNode node, IWorkflowNodeExecutionContext context)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var input in node.Inputs)
        {
            var name = input.Name.Trim();
            if (input.Source is { } source)
                values[name] = context.ResolveDynamicInput(name, WorkflowInput<object?>.FromBinding(source));
            else if (input.TryGetManualValue(out var value, out var error))
                values[name] = value;
            else
                throw new InvalidOperationException($"节点 {node.Id} 的子流程输入 {name} 无效：{error}");
        }
        return values;
    }

    /// <summary>在子流程运行结束后读取各项输出的来源节点输出。</summary>
    /// <param name="node">复合节点。</param>
    /// <param name="childContext">刚结束的子流程上下文。</param>
    /// <returns>按输出名称读取的值集合。</returns>
    internal static WorkflowSubflowValues ResolveOutputs(IWorkflowSubflowNode node, WorkflowContext childContext)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var output in node.Outputs)
        {
            var name = output.Name.Trim();
            var source = output.Source
                ?? throw new InvalidOperationException($"节点 {node.Id} 的子流程输出 {name} 未选择来源。");
            values[name] = WorkflowBindingResolver.ResolveLatest<object?>(childContext, source);
        }
        return new WorkflowSubflowValues(values);
    }
}

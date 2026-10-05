using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>运行 Block 子流程并应用显式输入、输出作用域映射。</summary>
public sealed class BlockNodeHandler : WorkflowNodeHandler<BlockNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        BlockNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.SubDocument.Graph.Nodes.Count == 0)
            throw new InvalidOperationException($"块节点 {node.Id} 的子画布为空。");
        if (context is not IWorkflowChildExecutionContext childExecution)
            throw new InvalidOperationException("当前运行时不支持受监管的子流程执行。");

        ValidateMappings(node);
        var definition = childExecution.GetChildWorkflowExecutionPlan();
        var childContext = childExecution.CreateChildScope();
        ApplyInputs(node, context, childContext);
        context.Trace("RunChild", $"运行已编译子流程入口 {definition.EntryNodeId}。", new Dictionary<string, object?>
        {
            ["ChildNodeCount"] = node.SubDocument.Graph.Nodes.Count,
            ["InputMappingCount"] = node.InputMappings.Count,
            ["OutputMappingCount"] = node.OutputMappings.Count
        });
        var result = await childExecution.RunChildWorkflowAsync(
            definition,
            childContext,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success)
            throw new InvalidOperationException($"块节点 {node.Id} 的子流程失败：{result.Message}");

        var mappedOutputs = ResolveOutputs(node, childContext);
        foreach (var pair in mappedOutputs)
            context.SetVariable(pair.Key, pair.Value);
        context.Trace("ChildCompleted", "子流程执行完成。", new Dictionary<string, object?>
        {
            ["ChildRunId"] = childContext.CurrentRunId,
            ["ElapsedMs"] = result.Elapsed.TotalMilliseconds,
            ["MappedOutputCount"] = mappedOutputs.Count
        });
        return NodeExecutionResult.Continue(output: new BlockNodeOutput(
            childContext.CurrentRunId,
            result.Elapsed,
            mappedOutputs));
    }

    private static void ApplyInputs(
        BlockNodeModel node,
        IWorkflowNodeExecutionContext parentContext,
        WorkflowContext childContext)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var mapping in node.InputMappings)
        {
            object? value = mapping.Source switch
            {
                E_BlockInputSource.Literal => parentContext.ResolveDynamicInput(
                    mapping.TargetVariableName,
                    WorkflowInput<object?>.FromLiteral(mapping.LiteralValue)),
                E_BlockInputSource.ParentVariable =>
                    parentContext.TryGetVariable<object>(mapping.ParentVariableName, out var variable)
                        ? variable
                        : throw new KeyNotFoundException($"父变量 {mapping.ParentVariableName} 不存在。"),
                E_BlockInputSource.ParentNodeBinding when mapping.ParentBinding.HasValue =>
                    parentContext.ResolveDynamicInput(
                        mapping.TargetVariableName,
                        WorkflowInput<object?>.FromBinding(mapping.ParentBinding.Value)),
                E_BlockInputSource.ParentNodeBinding =>
                    throw new InvalidOperationException($"输入映射 {mapping.TargetVariableName} 缺少 ParentBinding。"),
                _ => throw new NotSupportedException($"不支持的 Block 输入来源：{mapping.Source}。")
            };
            values[mapping.TargetVariableName] = value
                ?? throw new InvalidOperationException($"输入映射 {mapping.TargetVariableName} 的值为空；DP.WorkFlow Variables 不允许 null。");
        }
        foreach (var pair in values)
            childContext.SetVariable(pair.Key, pair.Value);
    }

    private static IReadOnlyDictionary<string, object> ResolveOutputs(
        BlockNodeModel node,
        WorkflowContext childContext)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var mapping in node.OutputMappings)
        {
            object? value = mapping.Source switch
            {
                E_BlockOutputSource.ChildVariable =>
                    childContext.TryGetVariable<object>(mapping.ChildVariableName, out var variable)
                        ? variable
                        : throw new KeyNotFoundException($"子变量 {mapping.ChildVariableName} 不存在。"),
                E_BlockOutputSource.ChildNodeBinding when mapping.ChildBinding.HasValue =>
                    WorkflowBindingResolver.ResolveLatest<object?>(childContext, mapping.ChildBinding.Value),
                E_BlockOutputSource.ChildNodeBinding =>
                    throw new InvalidOperationException($"输出映射 {mapping.TargetVariableName} 缺少 ChildBinding。"),
                _ => throw new NotSupportedException($"不支持的 Block 输出来源：{mapping.Source}。")
            };
            values[mapping.TargetVariableName] = value
                ?? throw new InvalidOperationException($"输出映射 {mapping.TargetVariableName} 的值为空；DP.WorkFlow Variables 不允许 null。");
        }
        return values;
    }

    private static void ValidateMappings(BlockNodeModel node)
    {
        ValidateTargetNames(node.InputMappings.Select(mapping => mapping.TargetVariableName), "输入");
        ValidateTargetNames(node.OutputMappings.Select(mapping => mapping.TargetVariableName), "输出");
        foreach (var mapping in node.InputMappings)
        {
            if (mapping.Source == E_BlockInputSource.ParentVariable
                && string.IsNullOrWhiteSpace(mapping.ParentVariableName))
            {
                throw new InvalidOperationException($"输入映射 {mapping.TargetVariableName} 缺少 ParentVariableName。");
            }
        }
        foreach (var mapping in node.OutputMappings)
        {
            if (mapping.Source == E_BlockOutputSource.ChildVariable
                && string.IsNullOrWhiteSpace(mapping.ChildVariableName))
            {
                throw new InvalidOperationException($"输出映射 {mapping.TargetVariableName} 缺少 ChildVariableName。");
            }
        }
    }

    private static void ValidateTargetNames(IEnumerable<string> names, string category)
    {
        var normalized = names.ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Block {category}映射的目标变量名不能为空。");
        var duplicate = normalized.GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Block {category}映射存在重复目标变量：{duplicate.Key}。");
    }
}

/// <summary>Block 节点完成后提交的子运行摘要。</summary>
public sealed record BlockNodeOutput(
    [property: DisplayName("子流程运行标识")] Guid ChildRunId,
    [property: DisplayName("耗时")] TimeSpan Elapsed,
    [property: DisplayName("映射输出")] IReadOnlyDictionary<string, object> MappedOutputs);

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>运行 Block 子流程：输入传入子画布的启动节点，输出按名称从子画布节点取回。</summary>
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

        var definition = childExecution.GetChildWorkflowExecutionPlan();
        var childContext = childExecution.CreateChildScope();
        childContext.SetSubflowInputs(WorkflowSubflowRuntime.ResolveInputs(node, context));
        context.Trace("RunChild", $"运行已编译子流程入口 {definition.EntryNodeId}。", new Dictionary<string, object?>
        {
            ["ChildNodeCount"] = node.SubDocument.Graph.Nodes.Count,
            ["InputCount"] = node.Inputs.Count,
            ["OutputCount"] = node.Outputs.Count
        });
        var result = await childExecution.RunChildWorkflowAsync(
            definition,
            childContext,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success)
            throw new InvalidOperationException($"块节点 {node.Id} 的子流程失败：{result.Message}");

        var outputs = WorkflowSubflowRuntime.ResolveOutputs(node, childContext);
        context.Trace("ChildCompleted", "子流程执行完成。", new Dictionary<string, object?>
        {
            ["ChildRunId"] = childContext.CurrentRunId,
            ["ElapsedMs"] = result.Elapsed.TotalMilliseconds,
            ["OutputCount"] = node.Outputs.Count
        });
        return NodeExecutionResult.Continue(output: new BlockNodeOutput(childContext.CurrentRunId, result.Elapsed, outputs));
    }
}

/// <summary>Block 节点完成后提交的输出；子流程接口中的各项输出按名称读取（例如“子流程块1.结果”）。</summary>
public sealed class BlockNodeOutput : IWorkflowKeyedOutput, IWorkflowResultRows
{
    private readonly WorkflowSubflowValues _outputs;

    /// <summary>创建 Block 输出。</summary>
    /// <param name="childRunId">子流程运行标识。</param>
    /// <param name="elapsed">子流程耗时。</param>
    /// <param name="outputs">子流程接口中的各项输出。</param>
    public BlockNodeOutput(Guid childRunId, TimeSpan elapsed, WorkflowSubflowValues outputs)
    {
        ChildRunId = childRunId;
        Elapsed = elapsed;
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
    }

    /// <summary>子流程运行标识。</summary>
    [DisplayName("子流程运行标识")]
    public Guid ChildRunId { get; }

    /// <summary>子流程耗时。</summary>
    [DisplayName("耗时")]
    public TimeSpan Elapsed { get; }

    /// <inheritdoc />
    [DisplayName("各项输出")]
    [JsonIgnore]
    public IReadOnlyList<KeyValuePair<string, object?>> ResultRows => _outputs.ResultRows;

    /// <inheritdoc />
    public bool TryGetKeyedValue(string key, out object? value) => _outputs.TryGetKeyedValue(key, out value);

    /// <inheritdoc />
    public string DescribeMissingKey(string key) => $"子流程没有名为 {key} 的输出。";
}

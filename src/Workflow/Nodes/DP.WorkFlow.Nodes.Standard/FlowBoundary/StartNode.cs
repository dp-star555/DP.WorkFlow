using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>
/// 表示工作流的标准启动节点。位于子画布中时，所在复合节点声明的输入作为它的输出成员，
/// 子画布中的节点绑定“启动节点.名称”读取。
/// </summary>
[WorkflowNode("Start", DisplayName = "启动节点", Category = "1.Base/流程边界")]
public sealed class StartNodeModel : WorkflowNodeModel, IWorkflowSubflowEntryNode, IWorkflowDynamicOutputNode
{
    /// <inheritdoc />
    public override string NodeType => "Start";

    /// <inheritdoc />
    [JsonIgnore]
    [Browsable(false)]
    public IReadOnlyList<WorkflowBindableMember> SubflowInputs { get; set; } = Array.Empty<WorkflowBindableMember>();

    /// <inheritdoc />
    /// <remarks>不在子画布中（没有输入）时不提供任何可绑定成员。</remarks>
    public IReadOnlyList<WorkflowBindableMember> DescribeOutputs() => SubflowInputs.Count == 0
        ? Array.Empty<WorkflowBindableMember>()
        : SubflowInputs.Prepend(new WorkflowBindableMember("$", typeof(WorkflowSubflowValues), "$")).ToArray();
}

/// <summary>执行标准启动节点。</summary>
public sealed class StartNodeHandler : WorkflowNodeHandler<StartNodeModel>
{
    /// <inheritdoc />
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        StartNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("Start", "流程由启动节点进入。");
        // 子流程中把所在复合节点传入的输入作为输出，供子画布中的节点按名称绑定。
        var inputs = context.SubflowInputs.Count == 0
            ? WorkflowSubflowValues.Empty
            : new WorkflowSubflowValues(context.SubflowInputs);
        return ValueTask.FromResult(NodeExecutionResult.Continue(output: inputs));
    }
}

using System.ComponentModel;

namespace DP.WorkFlow;

/// <summary>指定判断节点的条件来源。名称沿用旧版。</summary>
public enum E_DecisionConditionSource
{
    /// <summary>调用宿主注册的条件函数。</summary>
    Function = 0,

    /// <summary>解析固定值或上游节点输出绑定。</summary>
    Binding = 1
}

/// <summary>表示判断节点产生的标准输出。</summary>
public sealed record DecisionNodeResult([property: DisplayName("判定结果")] bool Value);

/// <summary>
/// 根据布尔条件选择 True 或 False 出口。
/// </summary>
[WorkflowNode("Decision", DisplayName = "函数判断节点", Category = "2.Function/判断")]
public sealed class DecisionNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "Decision";

    /// <summary>获取或设置条件来源。</summary>
    public E_DecisionConditionSource ConditionSource { get; set; }

    /// <summary>获取或设置宿主条件函数键。</summary>
    public string FunctionKey { get; set; } = string.Empty;

    /// <summary>获取或设置固定值/上游绑定形式的条件。</summary>
    public WorkflowInput<bool> Condition { get; set; } = WorkflowInput<bool>.FromLiteral(false);
}

/// <summary>执行布尔判断节点。</summary>
public sealed class DecisionNodeHandler : WorkflowNodeHandler<DecisionNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        DecisionNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        bool value;
        if (node.ConditionSource == E_DecisionConditionSource.Function)
        {
            var registry = context.GetRequiredCapability<IWorkflowConditionRegistry>();
            value = await registry.GetOrThrow(node.FunctionKey)(context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            value = context.ResolveInput(node.Condition);
        }

        var port = value ? WorkflowPorts.True : WorkflowPorts.False;
        context.Trace("Decision", $"判断结果为 {value}，选择 {port} 端口。", new Dictionary<string, object?>
        {
            ["Value"] = value,
            ["Port"] = port
        });
        return NodeExecutionResult.Continue(port, new DecisionNodeResult(value));
    }
}

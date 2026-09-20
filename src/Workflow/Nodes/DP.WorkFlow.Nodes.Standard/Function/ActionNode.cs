using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>
/// 通过稳定的函数键调用宿主动作。
/// </summary>
[WorkflowNode("Action", DisplayName = "函数节点", Category = "2.Function/执行")]
public sealed class ActionNodeModel : WorkflowNodeModel
{
    /// <inheritdoc />
    public override string NodeType => "Action";

    /// <summary>获取或设置需要调用的函数键。</summary>
    public string FunctionKey { get; set; } = string.Empty;
}

/// <summary>
/// 保存 Action 节点可调用的宿主动作。
/// </summary>
public interface IWorkflowActionRegistry
{
    /// <summary>获取指定动作；不存在时抛出异常。</summary>
    WorkflowActionDelegate GetOrThrow(string functionKey);
}

/// <summary>表示 Action 节点可调用的异步宿主动作。</summary>
public delegate ValueTask<object?> WorkflowActionDelegate(
    IWorkflowNodeExecutionContext context,
    CancellationToken cancellationToken);

/// <summary>
/// 提供线程安全的默认动作注册表实现。
/// </summary>
public sealed class WorkflowActionRegistry : IWorkflowActionRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowActionDelegate> _actions = new(StringComparer.Ordinal);

    /// <summary>注册或替换动作。</summary>
    public WorkflowActionRegistry Register(string functionKey, WorkflowActionDelegate action)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
            throw new ArgumentException("函数键不能为空。", nameof(functionKey));
        ArgumentNullException.ThrowIfNull(action);
        _actions[functionKey.Trim()] = action;
        return this;
    }

    /// <inheritdoc />
    public WorkflowActionDelegate GetOrThrow(string functionKey)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
            throw new InvalidOperationException("Action 节点没有配置 FunctionKey。");

        return _actions.TryGetValue(functionKey.Trim(), out var action)
            ? action
            : throw new KeyNotFoundException($"找不到工作流动作：{functionKey}。");
    }
}

/// <summary>执行函数节点。</summary>
public sealed class ActionNodeHandler : WorkflowNodeHandler<ActionNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        ActionNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var registry = context.GetRequiredCapability<IWorkflowActionRegistry>();

        context.Trace("Invoke", "开始调用工作流动作。", new Dictionary<string, object?>
        {
            ["FunctionKey"] = node.FunctionKey
        });
        var output = await registry.GetOrThrow(node.FunctionKey)(context, cancellationToken).ConfigureAwait(false);
        context.Trace("Invoked", "工作流动作调用完成。", new Dictionary<string, object?>
        {
            ["FunctionKey"] = node.FunctionKey
        });
        return NodeExecutionResult.Continue(output: output);
    }
}

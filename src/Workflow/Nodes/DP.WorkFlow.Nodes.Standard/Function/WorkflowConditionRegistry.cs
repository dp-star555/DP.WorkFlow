using System.Collections.Concurrent;

namespace DP.WorkFlow;

/// <summary>表示可由判断或等待节点调用的异步布尔条件。</summary>
public delegate ValueTask<bool> WorkflowConditionDelegate(
    IWorkflowNodeExecutionContext context,
    CancellationToken cancellationToken);

/// <summary>保存宿主提供的布尔条件函数。</summary>
public interface IWorkflowConditionRegistry
{
    /// <summary>获取指定条件；不存在时抛出异常。</summary>
    WorkflowConditionDelegate GetOrThrow(string functionKey);
}

/// <summary>提供线程安全的默认条件注册表。</summary>
public sealed class WorkflowConditionRegistry : IWorkflowConditionRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowConditionDelegate> _conditions = new(StringComparer.Ordinal);

    /// <summary>注册或替换条件。</summary>
    public WorkflowConditionRegistry Register(string functionKey, WorkflowConditionDelegate condition)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
            throw new ArgumentException("条件函数键不能为空。", nameof(functionKey));
        ArgumentNullException.ThrowIfNull(condition);
        _conditions[functionKey.Trim()] = condition;
        return this;
    }

    /// <inheritdoc />
    public WorkflowConditionDelegate GetOrThrow(string functionKey)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
            throw new InvalidOperationException("条件节点没有配置 FunctionKey。");
        return _conditions.TryGetValue(functionKey.Trim(), out var condition)
            ? condition
            : throw new KeyNotFoundException($"找不到工作流条件：{functionKey}。");
    }
}

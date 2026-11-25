using System.Collections.Concurrent;
using System.ComponentModel;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>模拟工位动作的输出。</summary>
/// <param name="Count">第几次执行。</param>
/// <param name="ElapsedMs">本次耗时。</param>
public sealed record SampleStationResult(
    [property: DisplayName("次数")] int Count,
    [property: DisplayName("耗时 ms")] int ElapsedMs);

/// <summary>
/// 模拟工位动作：等一段时间（上下浮动 20%）后完成；可设置每隔几次报一条提示、每隔几次失败一次（用来看故障处理）。
/// 不连接任何设备。
/// </summary>
[WorkflowNode(SampleStationNodeModel.Type, DisplayName = "模拟工位动作", Category = "9.示例/模拟设备", OutputType = typeof(SampleStationResult))]
public sealed class SampleStationNodeModel : WorkflowNodeModel
{
    /// <summary>节点类型。</summary>
    public const string Type = "Sample.Station";

    /// <inheritdoc />
    public override string NodeType => Type;

    /// <summary>动作时间。</summary>
    [WorkflowProperty("动作时间", "模拟动作耗时，实际会上下浮动 20%。", Category = "模拟", Unit = "ms")]
    public int DurationMs { get; set; } = 400;

    /// <summary>每隔几次报一条提示；0 表示不报。</summary>
    [WorkflowProperty("每隔几次提示", "每执行这么多次报一条“节拍提醒”；0 表示不报。", Category = "模拟")]
    public int NoticeEvery { get; set; }

    /// <summary>每隔几次失败一次；0 表示不失败。</summary>
    [WorkflowProperty("每隔几次失败", "每执行这么多次失败一次，流程进入故障处理；0 表示不失败。", Category = "模拟")]
    public int FailEvery { get; set; }
}

/// <summary>模拟工位动作的执行。失败后“就地恢复”直接按完成继续。</summary>
public sealed class SampleStationNodeHandler : WorkflowNodeHandler<SampleStationNodeModel>, IWorkflowInPlaceRecoveryNodeHandler
{
    // 次数按“这次运行 + 节点”记：循环的每一圈是同一次运行。
    private readonly ConcurrentDictionary<(Guid Run, string Node), int> _counters = new();
    private readonly Random _random = new();

    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(SampleStationNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
    {
        var count = _counters.AddOrUpdate((context.ExecutionIdentity.RunId, node.Id), 1, (_, value) => value + 1);
        int duration;
        lock (_random) duration = Math.Max(0, (int)(node.DurationMs * (0.8 + _random.NextDouble() * 0.4)));
        using (var wait = context.BeginWait($"{node.Title} {duration} ms"))
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            wait.End(satisfied: true);
        }
        if (node.NoticeEvery > 0 && count % node.NoticeEvery == 0)
            context.RaiseNotice("节拍提醒", $"第 {count} 次动作用了 {duration} ms（设定 {node.DurationMs} ms），请留意节拍。");
        if (node.FailEvery > 0 && count % node.FailEvery == 0)
            return NodeExecutionResult.Fail($"模拟故障：第 {count} 次动作没有收到完成信号。");
        return NodeExecutionResult.Continue(output: new SampleStationResult(count, duration));
    }

    /// <inheritdoc />
    public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
        WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Trace("就地恢复", "人工确认动作已完成，按完成继续。");
        return new ValueTask<NodeExecutionResult>(NodeExecutionResult.Continue(output: new SampleStationResult(0, 0)));
    }
}

/// <summary>把模拟节点登记进节点目录。</summary>
public sealed class SampleNodesModule : IWorkflowRuntimePluginModule
{
    /// <inheritdoc />
    public string ExtensionId => "Samples.Pages";

    /// <inheritdoc />
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<SampleStationNodeModel, SampleStationResult>(
            ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
        extensions.Handlers.Register(new SampleStationNodeHandler());
    }
}

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DP.WorkFlow;

/// <summary>
/// 反复运行一个子流程画布，适合设备的长期循环扫描：循环块之前的节点只执行一次，子画布每圈运行一次。
/// 每圈是一次独立的子运行，执行次数上限、恢复次数和运行记录按圈计算，上一圈的输出和资源在下一圈开始前释放。
/// </summary>
[WorkflowNode("LoopBlock", DisplayName = "循环块", Category = "Control", Description = "反复运行一个隔离的子流程画布，按次数或退出变量结束。")]
public sealed class LoopBlockNodeModel : WorkflowNodeModel, IWorkflowSubflowNode, IWorkflowDynamicOutputNode, IWorkflowBindingDeclarationProvider
{
    /// <inheritdoc />
    public override string NodeType => "LoopBlock";

    private WorkflowDocument _subDocument = new();

    /// <inheritdoc />
    [JsonIgnore]
    public WorkflowDocument SubDocument
    {
        get => _subDocument;
        set => _subDocument = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>获取或设置子流程输入；进入循环时解析一次，每圈都以相同的值传入。</summary>
    [Browsable(false)]
    public IList<WorkflowSubflowInput> Inputs { get; set; } = new List<WorkflowSubflowInput>();

    /// <summary>获取或设置子流程输出；退出循环时从最后一圈取值。</summary>
    [Browsable(false)]
    public IList<WorkflowSubflowOutput> Outputs { get; set; } = new List<WorkflowSubflowOutput>();

    /// <inheritdoc />
    [JsonIgnore]
    [Browsable(false)]
    public IReadOnlyList<WorkflowBindableMember> SubflowOutputs { get; set; } = Array.Empty<WorkflowBindableMember>();

    /// <summary>获取或设置子流程运行的圈数；0 表示一直循环。</summary>
    [WorkflowProperty("循环次数", "子流程运行的圈数；0 表示一直循环，直到退出变量为真、子流程失败或流程停止。", Category = "循环")]
    public int Iterations { get; set; }

    /// <summary>获取或设置退出变量名；一圈结束时该子流程变量为 true 则退出循环。</summary>
    [WorkflowProperty("退出变量", "子流程中的布尔变量名；一圈结束时该变量为真则退出循环。为空时不按变量退出。", Category = "循环")]
    public string ExitVariableName { get; set; } = string.Empty;

    /// <summary>获取子画布节点数量。</summary>
    [JsonIgnore]
    public int ChildNodeCount => SubDocument.Graph.Nodes.Count;

    /// <inheritdoc />
    public IReadOnlyList<WorkflowBindableMember> DescribeOutputs() =>
        WorkflowSubflowInterfaces.DescribeCompositeOutputs(this, typeof(LoopBlockNodeOutput));

    /// <inheritdoc />
    public IEnumerable<WorkflowDeclaredBinding> GetDeclaredBindings() => WorkflowSubflowInterfaces.DeclareBindings(this);
}

/// <summary>按圈运行循环块子流程，直到达到次数、退出变量为真、子流程失败或被取消。</summary>
public sealed class LoopBlockNodeHandler : WorkflowNodeHandler<LoopBlockNodeModel>
{
    /// <inheritdoc />
    protected override async ValueTask<NodeExecutionResult> ExecuteAsync(
        LoopBlockNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.SubDocument.Graph.Nodes.Count == 0)
            throw new InvalidOperationException($"循环块 {node.Id} 的子画布为空。");
        if (node.Iterations < 0)
            throw new InvalidOperationException($"循环块 {node.Id} 的循环次数不能小于 0。");
        if (context is not IWorkflowChildExecutionContext childExecution)
            throw new InvalidOperationException("当前运行时不支持受监管的子流程执行。");

        var exitVariable = string.IsNullOrWhiteSpace(node.ExitVariableName) ? null : node.ExitVariableName.Trim();
        var definition = childExecution.GetChildWorkflowExecutionPlan();
        var childContext = childExecution.CreateChildScope();
        // 父流程在循环期间停在本节点，输入值不会变化：只解析一次，每圈由启动节点以相同的值提供。
        childContext.SetSubflowInputs(WorkflowSubflowRuntime.ResolveInputs(node, context));
        context.Trace("RunLoop", $"循环运行子流程入口 {definition.EntryNodeId}。", new Dictionary<string, object?>
        {
            ["Iterations"] = node.Iterations,
            ["ExitVariable"] = exitVariable,
            ["ChildNodeCount"] = node.SubDocument.Graph.Nodes.Count
        });

        var started = DateTimeOffset.UtcNow;
        var cycles = 0;
        string exitReason;
        await using (var session = childExecution.OpenChildWorkflowSession(definition, childContext))
        {
            while (true)
            {
                if (cycles > 0)
                    ClearVariables(childContext);
                var result = await session.RunCycleAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.Success)
                    throw new InvalidOperationException($"循环块 {node.Id} 第 {cycles + 1} 圈子流程失败：{result.Message}");
                cycles++;
                if (exitVariable is not null && childContext.TryGetVariable<bool>(exitVariable, out var exit) && exit)
                {
                    exitReason = "退出变量为真";
                    break;
                }
                if (node.Iterations > 0 && cycles >= node.Iterations)
                {
                    exitReason = "达到循环次数";
                    break;
                }
            }
            // 最后一圈的输出可能作为本节点的输出被读取（例如图像帧），其资源随本节点输出的生命周期释放。
            session.KeepLastCycleOutputs();
        }

        var outputs = WorkflowSubflowRuntime.ResolveOutputs(node, childContext);
        var elapsed = DateTimeOffset.UtcNow - started;
        context.Trace("LoopCompleted", $"循环结束：{exitReason}，共 {cycles} 圈。", new Dictionary<string, object?>
        {
            ["Cycles"] = cycles,
            ["ExitReason"] = exitReason,
            ["LastChildRunId"] = childContext.CurrentRunId,
            ["ElapsedMs"] = elapsed.TotalMilliseconds,
            ["OutputCount"] = node.Outputs.Count
        });
        return NodeExecutionResult.Continue(output: new LoopBlockNodeOutput(
            cycles,
            exitReason,
            childContext.CurrentRunId,
            elapsed,
            outputs));
    }

    /// <summary>圈与圈之间不传递数据：清除上一圈写入的子流程变量。</summary>
    private static void ClearVariables(WorkflowContext childContext)
    {
        foreach (var key in childContext.ExportVariables().Keys)
            childContext.RemoveVariable(key);
    }
}

/// <summary>循环块退出后提交的输出；子流程接口中的各项输出取自最后一圈，按名称读取（例如“循环块1.结果”）。</summary>
public sealed class LoopBlockNodeOutput : IWorkflowKeyedOutput, IWorkflowResultRows
{
    private readonly WorkflowSubflowValues _outputs;

    /// <summary>创建循环块输出。</summary>
    /// <param name="completedCycles">完成圈数。</param>
    /// <param name="exitReason">退出原因。</param>
    /// <param name="lastChildRunId">最后一圈的子流程运行标识。</param>
    /// <param name="elapsed">整个循环的耗时。</param>
    /// <param name="outputs">最后一圈的各项输出。</param>
    public LoopBlockNodeOutput(int completedCycles, string exitReason, Guid lastChildRunId, TimeSpan elapsed, WorkflowSubflowValues outputs)
    {
        CompletedCycles = completedCycles;
        ExitReason = exitReason;
        LastChildRunId = lastChildRunId;
        Elapsed = elapsed;
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
    }

    /// <summary>完成圈数。</summary>
    [DisplayName("完成圈数")]
    public int CompletedCycles { get; }

    /// <summary>退出原因。</summary>
    [DisplayName("退出原因")]
    public string ExitReason { get; }

    /// <summary>最后一圈的子流程运行标识。</summary>
    [DisplayName("最后一圈运行标识")]
    public Guid LastChildRunId { get; }

    /// <summary>整个循环的耗时。</summary>
    [DisplayName("耗时")]
    public TimeSpan Elapsed { get; }

    /// <inheritdoc />
    [DisplayName("各项输出")]
    [JsonIgnore]
    public IReadOnlyList<KeyValuePair<string, object?>> ResultRows => _outputs.ResultRows;

    /// <inheritdoc />
    public bool TryGetKeyedValue(string key, out object? value) => _outputs.TryGetKeyedValue(key, out value);

    /// <inheritdoc />
    public string DescribeMissingKey(string key) => $"循环块没有名为 {key} 的输出。";
}

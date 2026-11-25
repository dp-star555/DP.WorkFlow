namespace DP.WorkFlow.Tests.Replay;

/// <summary>
/// 回放基准用的典型流程和模拟设备。流程只用标准节点、复合节点和一个带失败出口的检测节点，
/// 设备动作全部是确定性的（不依赖时间、随机数），同一引擎反复运行得到相同的事件序列。
/// </summary>
/// <remarks>
/// 这里的构造代码只用于生成 Flows/ 中的流程文件；基准测试运行的是文件本身，
/// 以后流程文件格式变化时，旧文件仍按原样保留，用来验证旧文件能打开并得到相同结果。
/// </remarks>
public static class ReplayFlows
{
    /// <summary>全部典型流程：名称 → 构造方法。名称同时是流程文件名和基准文件名。</summary>
    public static IReadOnlyDictionary<string, Func<WorkflowDocument>> All { get; } =
        new Dictionary<string, Func<WorkflowDocument>>(StringComparer.Ordinal)
        {
            ["sequence-binding"] = SequenceAndBinding,
            ["canvas-loop"] = CanvasLoop,
            ["parallel-merge"] = ParallelMerge,
            ["loop-with-parallel"] = LoopWithParallel,
            ["failed-port"] = FailedPort,
            ["node-fault"] = NodeFault,
            ["block-subflow"] = BlockSubflow,
            ["loop-block"] = LoopBlock
        };

    public static WorkflowNodeCatalog CreateCatalog() =>
        new WorkflowNodeCatalog()
            .RegisterStandardNodes()
            .RegisterCompositeNodes()
            .Register<InspectNodeModel>(
                1,
                WorkflowPortDescriptor.Input(maxConnections: int.MaxValue),
                WorkflowPortDescriptor.Output(),
                WorkflowPortDescriptor.Failure());

    public static WorkflowNodeHandlerCatalog CreateHandlers() =>
        new WorkflowNodeHandlerCatalog()
            .RegisterStandardNodeHandlers()
            .RegisterCompositeNodeHandlers()
            .Register(new InspectNodeHandler());

    /// <summary>模拟设备：每个流程一份新的动作表，计数从零开始。</summary>
    public static IServiceProvider CreateDevices()
    {
        var counter = 0L;
        var actions = new WorkflowActionRegistry()
            .Register("Pass", (_, _) => ValueTask.FromResult<object?>(null))
            .Register("Measure", (_, _) => ValueTask.FromResult<object?>(new MeasureOutput(21, true)))
            .Register("Count", (_, _) => ValueTask.FromResult<object?>(new CountOutput(Interlocked.Increment(ref counter))))
            .Register("Double", (context, _) =>
                ValueTask.FromResult<object?>(new CountOutput(Convert.ToInt64(context.SubflowInputs["Value"]) * 2)))
            .Register("Boom", (_, _) => throw new InvalidOperationException("模拟设备故障：伺服报警。"));
        return new WorkflowServiceProvider().Add<IWorkflowActionRegistry>(actions);
    }

    /// <summary>开始 → 测量 → 判断（绑定 测量.Ok）→ 合格 / 不合格 → 结束。</summary>
    private static WorkflowDocument SequenceAndBinding()
    {
        var document = Document("顺序与绑定", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            Action("Measure", "测量", "Measure"),
            new DecisionNodeModel
            {
                Id = "Judge",
                Title = "判断",
                ConditionSource = E_DecisionConditionSource.Binding,
                Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Measure", "Ok"))
            },
            Action("Accept", "合格", "Pass"),
            Action("Reject", "不合格", "Pass"),
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Measure");
        Connect(document, "Measure", WorkflowPorts.Success, "Judge");
        Connect(document, "Judge", WorkflowPorts.True, "Accept");
        Connect(document, "Judge", WorkflowPorts.False, "Reject");
        Connect(document, "Accept", WorkflowPorts.Success, "End");
        Connect(document, "Reject", WorkflowPorts.Success, "End");
        return document;
    }

    /// <summary>开始 → 循环 3 次（计数）→ 结束。</summary>
    private static WorkflowDocument CanvasLoop()
    {
        var document = Document("画布循环", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 3 },
            Action("Body", "计数", "Count"),
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Loop");
        Connect(document, "Loop", WorkflowPorts.Loop, "Body");
        Connect(document, "Body", WorkflowPorts.Success, "Loop");
        Connect(document, "Loop", WorkflowPorts.Completed, "End");
        return document;
    }

    /// <summary>开始 → 并行（左相机 / 右相机）→ 汇聚 → 判断（绑定 左相机.Ok）→ 结束。</summary>
    private static WorkflowDocument ParallelMerge()
    {
        var document = Document("并行汇聚", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            new ParallelAllNodeModel { Id = "Parallel", Title = "并行" },
            Action("Left", "左相机", "Measure"),
            Action("Right", "右相机", "Pass"),
            new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" },
            new DecisionNodeModel
            {
                Id = "Judge",
                Title = "判断",
                ConditionSource = E_DecisionConditionSource.Binding,
                Condition = WorkflowInput<bool>.FromBinding(new WorkflowBindingKey("Left", "Ok"))
            },
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Parallel");
        Connect(document, "Parallel", WorkflowPorts.Branch, "Left");
        Connect(document, "Parallel", WorkflowPorts.Branch, "Right");
        Connect(document, "Left", WorkflowPorts.Success, "Merge");
        Connect(document, "Right", WorkflowPorts.Success, "Merge");
        Connect(document, "Merge", WorkflowPorts.Success, "Judge");
        Connect(document, "Judge", WorkflowPorts.True, "End");
        Connect(document, "Judge", WorkflowPorts.False, "End");
        return document;
    }

    /// <summary>循环 2 次，每圈并行两个分支后汇聚。</summary>
    private static WorkflowDocument LoopWithParallel()
    {
        var document = Document("循环中并行", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            new LoopNodeModel { Id = "Loop", Title = "循环", Iterations = 2 },
            new ParallelAllNodeModel { Id = "Parallel", Title = "并行" },
            Action("BranchA", "分支A", "Measure"),
            Action("BranchB", "分支B", "Pass"),
            new WaitAllInputsCompletedNodeModel { Id = "Merge", Title = "汇聚" },
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Loop");
        Connect(document, "Loop", WorkflowPorts.Loop, "Parallel");
        Connect(document, "Parallel", WorkflowPorts.Branch, "BranchA");
        Connect(document, "Parallel", WorkflowPorts.Branch, "BranchB");
        Connect(document, "BranchA", WorkflowPorts.Success, "Merge");
        Connect(document, "BranchB", WorkflowPorts.Success, "Merge");
        Connect(document, "Merge", WorkflowPorts.Success, "Loop");
        Connect(document, "Loop", WorkflowPorts.Completed, "End");
        return document;
    }

    /// <summary>检测失败沿失败出口走 NG 支路，运行照常完成。</summary>
    private static WorkflowDocument FailedPort()
    {
        var document = Document("失败出口", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            new InspectNodeModel { Id = "Inspect", Title = "检测", Fail = true },
            Action("Ok", "OK", "Pass"),
            Action("Ng", "NG", "Pass"),
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Inspect");
        Connect(document, "Inspect", WorkflowPorts.Success, "Ok");
        Connect(document, "Inspect", WorkflowPorts.Failed, "Ng");
        Connect(document, "Ok", WorkflowPorts.Success, "End");
        Connect(document, "Ng", WorkflowPorts.Success, "End");
        return document;
    }

    /// <summary>设备故障且没有失败出口：运行以失败结束。</summary>
    private static WorkflowDocument NodeFault()
    {
        var document = Document("节点故障", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            Action("Servo", "伺服", "Boom"),
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Servo");
        Connect(document, "Servo", WorkflowPorts.Success, "End");
        return document;
    }

    /// <summary>开始 → 计数 → 子流程块（输入绑定 计数.Value，输出 Doubled）→ 结束。</summary>
    private static WorkflowDocument BlockSubflow()
    {
        var child = Document("加倍子流程", "ChildStart");
        Add(child,
            new StartNodeModel { Id = "ChildStart", Title = "开始" },
            Action("Double", "加倍", "Double"),
            new EndNodeModel { Id = "ChildEnd", Title = "结束" });
        Connect(child, "ChildStart", WorkflowPorts.Success, "Double");
        Connect(child, "Double", WorkflowPorts.Success, "ChildEnd");

        var block = new BlockNodeModel { Id = "Block", Title = "子流程", SubDocument = child };
        block.Inputs.Add(new WorkflowSubflowInput { Name = "Value", Source = new WorkflowBindingKey("Counter", "Value") });
        block.Outputs.Add(new WorkflowSubflowOutput { Name = "Doubled", Source = new WorkflowBindingKey("Double", "Value") });

        var document = Document("子流程块", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            Action("Counter", "计数", "Count"),
            block,
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "Counter");
        Connect(document, "Counter", WorkflowPorts.Success, "Block");
        Connect(document, "Block", WorkflowPorts.Success, "End");
        return document;
    }

    /// <summary>开始 → 循环块 3 圈（每圈计数）→ 结束。</summary>
    private static WorkflowDocument LoopBlock()
    {
        var child = Document("循环体", "ChildStart");
        Add(child,
            new StartNodeModel { Id = "ChildStart", Title = "开始" },
            Action("Work", "计数", "Count"),
            new EndNodeModel { Id = "ChildEnd", Title = "结束" });
        Connect(child, "ChildStart", WorkflowPorts.Success, "Work");
        Connect(child, "Work", WorkflowPorts.Success, "ChildEnd");

        var loop = new LoopBlockNodeModel { Id = "LoopBlock", Title = "循环块", SubDocument = child, Iterations = 3 };
        var document = Document("循环块", "Start");
        Add(document,
            new StartNodeModel { Id = "Start", Title = "开始" },
            loop,
            new EndNodeModel { Id = "End", Title = "结束" });
        Connect(document, "Start", WorkflowPorts.Success, "LoopBlock");
        Connect(document, "LoopBlock", WorkflowPorts.Success, "End");
        return document;
    }

    private static WorkflowDocument Document(string name, string entry) =>
        new() { Name = name, EntryNodeId = entry };

    private static ActionNodeModel Action(string id, string title, string functionKey) =>
        new() { Id = id, Title = title, FunctionKey = functionKey };

    private static void Add(WorkflowDocument document, params IWorkflowNodeModel[] nodes)
    {
        var x = 0;
        foreach (var node in nodes)
        {
            // 文档模型不再自带默认尺寸；样例按设计器画出的流程写明尺寸，保存出的文件与版本库中的一致。
            document.Nodes.Add(new WorkflowCanvasNode { Node = node, X = x, Y = 0, Width = 180, Height = 60 });
            x += 220;
        }
    }

    private static void Connect(WorkflowDocument document, string from, string port, string to) =>
        document.Connections.Add(new WorkflowConnectionModel
        {
            FromNodeId = from,
            FromPort = port,
            ToNodeId = to,
            ToPort = WorkflowPorts.Input
        });
}

/// <summary>测量结果：数值和是否合格。</summary>
public sealed record MeasureOutput(long Value, bool Ok);

/// <summary>计数结果。</summary>
public sealed record CountOutput(long Value);

/// <summary>带失败出口的模拟检测节点。</summary>
[WorkflowNode("ReplayInspect", DisplayName = "模拟检测")]
public sealed class InspectNodeModel : WorkflowNodeModel
{
    public override string NodeType => "ReplayInspect";

    /// <summary>为真时检测失败（NG）。</summary>
    public bool Fail { get; set; }
}

public sealed class InspectNodeHandler : WorkflowNodeHandler<InspectNodeModel>
{
    protected override ValueTask<NodeExecutionResult> ExecuteAsync(
        InspectNodeModel node,
        IWorkflowNodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.Fail)
            throw new InvalidOperationException("检测NG：未找到边缘。");
        return ValueTask.FromResult(NodeExecutionResult.Continue());
    }
}

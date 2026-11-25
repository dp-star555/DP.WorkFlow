using DP.WorkFlow.UI;

namespace DP.WorkFlow.Samples;

/// <summary>两个桌面宿主共用的恢复演示。仅模拟位置与命令次数，不连接任何物理设备。</summary>
public sealed class WorkflowRecoveryDemo : IWorkflowRuntimePluginModule
{
    private WarningHandlerBlockNodeModel? _warning;
    private Guid _runId;
    private int _target;
    private int _position = 100;
    private int _commands;
    private int _feeds;
    private bool _lostResponse;

    /// <inheritdoc />
    public string ExtensionId => "Samples.Recovery";

    /// <inheritdoc />
    public void Register(WorkflowRuntimePluginCatalog extensions)
    {
        extensions.Nodes.Register(WorkflowNodeDescriptor.Create<FeedNode, int>(ports: Ports()))
            .Register(WorkflowNodeDescriptor.Create<MoveNode, MoveResult>(ports: Ports()));
        extensions.Handlers.Register(new FeedHandler(this)).Register(new MoveHandler(this));
    }

    /// <summary>填充一个只有Start的工作区，包含可编辑的独立处理子文档。</summary>
    /// <param name="session">新工作区根会话。</param>
    public void Populate(WorkflowDesignerSession session)
    {
        var feed = session.AddNode("Sample.RecoveryFeed", 260, 80).Node;
        var entry = (RecoveryEntryNodeModel)session.AddNode("RecoveryEntry", 470, 80).Node;
        entry.EntryKey = "OperationStart";
        entry.Title = "本次操作入口";
        var move = session.AddNode("Sample.RecoveryMove", 690, 80).Node;
        var end = session.AddNode("End", 920, 80).Node;
        session.Connect("Start", WorkflowPorts.Success, feed.Id);
        session.Connect(feed.Id, WorkflowPorts.Success, entry.Id);
        session.Connect(entry.Id, WorkflowPorts.Success, move.Id);
        session.Connect(move.Id, WorkflowPorts.Success, end.Id);
        _warning = (WarningHandlerBlockNodeModel)session.AddNode("WarningHandlerBlock", 270, 300).Node;
        _warning.Title = "工程师预设的模拟处置";
        var document = _warning.SubDocument;
        document.Nodes.Clear();
        document.Connections.Clear();
        Add(document, new WarningHandlerStartNodeModel { Id = "HandlerStart", Title = "处理入口" }, 60, 160);
        Add(document, new OperatorChoiceNodeModel
        {
            Id = "Choice", Title = "模拟轴异常：选择处理方案",
            Message = "这是软件模拟，不操作真实设备。\n模拟轴从100移动到110，但首次完成响应丢失。\n\n就地恢复：核实已到110，不再发送位移。\n回初始点重做：人工确认后把模拟位置置0，再回退到操作入口重新执行。",
            OptionsText = "就地恢复=Continue;回初始点重做=Restart;停止=Stop"
        }, 300, 160);
        Add(document, new RecoverInPlaceNodeModel { Id = "Continue", Title = "就地恢复" }, 570, 20);
        Add(document, new OperatorStepConfirmNodeModel
        {
            Id = "Confirm", Title = "确认模拟回初始点",
            StepText = "点击处理完成后，会把软件模拟位置设为0，并重新运行本次操作段。\n不会重复进料；不会向真实设备发送任何命令。"
        }, 570, 160);
        Add(document, new ActionNodeModel { Id = "Reset", Title = "模拟位置置0", FunctionKey = "RecoveryDemo.Reset" }, 800, 160);
        Add(document, new RollBackNodeModel { Id = "Restart", Title = "回退到操作入口再继续", TargetsText = "OperationStart" }, 1030, 160);
        Add(document, new StopCurrentStationNodeModel { Id = "Stop", Title = "停止演示", Reason = "操作员选择停止模拟流程。" }, 570, 320);
        document.EntryNodeId = "HandlerStart";
        Connect(document, "HandlerStart", "Choice");
        Connect(document, "Choice", "Continue", "Continue");
        Connect(document, "Choice", "Confirm", "Restart");
        Connect(document, "Confirm", "Reset");
        Connect(document, "Reset", "Restart");
        Connect(document, "Choice", "Stop", "Stop");
    }

    /// <summary>装配模拟动作；人工服务仍由桌面宿主提供。处理流程由统管层在故障时从当前文档的处理块里找。</summary>
    /// <param name="services">宿主能力容器。</param>
    /// <param name="nodes">已注册的节点目录。</param>
    /// <param name="handlers">已注册的处理器目录。</param>
    /// <param name="actions">宿主动作注册表。</param>
    public void ConfigureServices(WorkflowServiceProvider services, WorkflowNodeCatalog nodes,
        WorkflowNodeHandlerCatalog handlers, WorkflowActionRegistry actions)
    {
        if (_warning is null) throw new InvalidOperationException("请先创建恢复演示文档。");
        actions.Register("RecoveryDemo.Reset", (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            _position = 0;
            return new ValueTask<object?>(0);
        });
        services.Add<IWorkflowActionRegistry>(actions);
    }

    /// <summary>把运行宿主交给统管层：模拟轴故障时由故障会话运行文档里的处理块。</summary>
    /// <param name="host">已创建的运行宿主。</param>
    /// <param name="services">宿主能力（处理流程的人工服务从这里取）。</param>
    /// <param name="nodes">节点目录。</param>
    /// <param name="handlers">处理器目录。</param>
    /// <returns>调用方负责释放的流程管理。</returns>
    public WorkflowRunCenter AttachFaultHandling(WorkflowRuntimeHost host, IServiceProvider services,
        WorkflowNodeCatalog nodes, WorkflowNodeHandlerCatalog handlers)
    {
        // 处理流程出错时的兜底：用宿主的人工交互服务问操作员（重新运行处理流程 / 直接给去向 / 停止）。
        var options = new WorkflowRunCenterOptions();
        if (services.GetService(typeof(IWorkflowOperatorService)) is IWorkflowOperatorService operators)
            options.FaultFallback = new WorkflowOperatorFaultFallback(operators);
        var center = new WorkflowRunCenter(nodes, handlers, options, services);
        center.AttachHost("恢复演示", host);
        return center;
    }

    private static WorkflowPortDescriptor[] Ports() => new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() };
    private static void Add(WorkflowDocument document, WorkflowNodeModel node, double x, double y) =>
        document.Nodes.Add(new WorkflowCanvasNode { Node = node, X = x, Y = y });
    private static void Connect(WorkflowDocument document, string from, string to, string port = WorkflowPorts.Success) =>
        document.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = port, ToNodeId = to });

    /// <summary>模拟运动标准输出，可在运行监控中查看命令和进料次数。</summary>
    /// <param name="Position">模拟位置。</param>
    /// <param name="Commands">模拟命令次数。</param>
    /// <param name="Feeds">模拟进料次数。</param>
    public sealed record MoveResult(int Position, int Commands, int Feeds);

    [WorkflowNode("Sample.RecoveryFeed", DisplayName = "模拟进料", Category = "9.Samples/恢复演示")]
    private sealed class FeedNode : WorkflowNodeModel { public override string NodeType => "Sample.RecoveryFeed"; }
    [WorkflowNode("Sample.RecoveryMove", DisplayName = "模拟轴运动（首次丢响应）", Category = "9.Samples/恢复演示")]
    private sealed class MoveNode : WorkflowNodeModel { public override string NodeType => "Sample.RecoveryMove"; }

    private sealed class FeedHandler(WorkflowRecoveryDemo demo) : WorkflowNodeHandler<FeedNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(FeedNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            if (demo._runId != context.ExecutionIdentity.RunId)
            {
                demo._runId = context.ExecutionIdentity.RunId;
                demo._position = 100; demo._commands = 0; demo._feeds = 0; demo._lostResponse = false;
            }
            return new ValueTask<NodeExecutionResult>(NodeExecutionResult.Continue(output: ++demo._feeds));
        }
    }

    private sealed class MoveHandler(WorkflowRecoveryDemo demo) : WorkflowNodeHandler<MoveNode>, IWorkflowInPlaceRecoveryNodeHandler
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(MoveNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            demo._target = demo._position + 10;
            demo._commands++;
            demo._position = demo._target;
            context.ReportProgress("位移命令已发");
            if (!demo._lostResponse)
            {
                demo._lostResponse = true;
                return new ValueTask<NodeExecutionResult>(NodeExecutionResult.Fail("模拟轴完成响应丢失；设备实际状态须由处置逻辑核实。"));
            }
            return new ValueTask<NodeExecutionResult>(Completed(context));
        }

        // 就地恢复：模拟位置已经在目标上就按完成往下走，不再发位移。
        public ValueTask<NodeExecutionResult> RecoverInPlaceAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
            WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<NodeExecutionResult>(demo._position == demo._target
                ? Completed(context)
                : NodeExecutionResult.Fail("原目标不再满足，不能确认原操作完成。"));
        }

        private NodeExecutionResult Completed(IWorkflowNodeExecutionContext context)
        {
            var result = new MoveResult(demo._position, demo._commands, demo._feeds);
            context.Trace("模拟恢复结果", $"位置={result.Position}，命令次数={result.Commands}，进料次数={result.Feeds}");
            return NodeExecutionResult.Continue(output: result);
        }
    }
}

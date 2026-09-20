using DP.WorkFlow.UI;

namespace DP.WorkFlow.Samples;

/// <summary>两个桌面宿主共用的恢复演示。仅模拟位置与命令次数，不连接任何物理设备。</summary>
public sealed class WorkflowRecoveryDemo : IWorkflowRuntimePluginModule, IWorkflowRecoveryEntryGuard
{
    private WarningHandlerBlockNodeModel? _warning;
    private Guid _runId;
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
        document.CanvasProjection.Nodes.Clear();
        document.CanvasProjection.Connections.Clear();
        Add(document, new WarningHandlerStartNodeModel { Id = "HandlerStart", Title = "处理入口" }, 60, 160);
        Add(document, new OperatorChoiceNodeModel
        {
            Id = "Choice", Title = "模拟轴异常：选择处理方案",
            Message = "这是软件模拟，不操作真实设备。\n模拟轴从100移动到110，但首次完成响应丢失。\n\n继续原操作：核实已到110，不再发送位移。\n回初始点重做：人工确认后把模拟位置置0，再从操作入口重新执行。",
            OptionsText = "继续原操作=Continue;回初始点重做=Restart;停止=Stop"
        }, 300, 160);
        Add(document, new ContinueOperationNodeModel { Id = "Continue", Title = "继续原操作" }, 570, 20);
        Add(document, new OperatorStepConfirmNodeModel
        {
            Id = "Confirm", Title = "确认模拟回初始点",
            StepText = "点击处理完成后，会把软件模拟位置设为0，并重新运行本次操作段。\n不会重复进料；不会向真实设备发送任何命令。"
        }, 570, 160);
        Add(document, new ActionNodeModel { Id = "Reset", Title = "模拟位置置0", FunctionKey = "RecoveryDemo.Reset" }, 800, 160);
        Add(document, new RestartFromEntryNodeModel { Id = "Restart", Title = "从操作入口重执行", EntryKey = "OperationStart" }, 1030, 160);
        Add(document, new StopCurrentStationNodeModel { Id = "Stop", Title = "停止演示", Reason = "操作员选择停止模拟流程。" }, 570, 320);
        document.EntryNodeId = "HandlerStart";
        Connect(document, "HandlerStart", "Choice");
        Connect(document, "Choice", "Continue", "Continue");
        Connect(document, "Choice", "Confirm", "Restart");
        Connect(document, "Confirm", "Reset");
        Connect(document, "Reset", "Restart");
        Connect(document, "Choice", "Stop", "Stop");
    }

    /// <summary>装配模拟动作、处理策略与模拟工位Guard；人工服务仍由桌面宿主提供。</summary>
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
            return ValueTask.FromResult<object?>(0);
        });
        services.Add<IWorkflowActionRegistry>(actions)
            .Add<IWorkflowRecoveryEntryGuard>(this)
            .Add<IWorkflowFaultRecoveryCoordinator>(new WorkflowWarningHandlerCoordinator(_warning, nodes, handlers));
    }

    /// <summary>在新Run开始前使用当前文档重新编译处理策略，避免编辑后的子流程仍使用旧快照。</summary>
    /// <param name="document">当前根文档。</param>
    /// <param name="services">宿主能力。</param>
    /// <param name="nodes">节点目录。</param>
    /// <param name="handlers">处理器目录。</param>
    public void RebindTreatment(WorkflowDocument document, WorkflowServiceProvider services,
        WorkflowNodeCatalog nodes, WorkflowNodeHandlerCatalog handlers)
    {
        var blocks = document.Graph.Nodes.OfType<WarningHandlerBlockNodeModel>().ToArray();
        if (blocks.Length != 1) throw new InvalidOperationException("恢复演示需要且只允许一个异常处理块。");
        services.Add<IWorkflowFaultRecoveryCoordinator>(new WorkflowWarningHandlerCoordinator(blocks[0], nodes, handlers));
    }

    /// <inheritdoc />
    public ValueTask<WorkflowRecoveryEntryValidation> ValidateAsync(WorkflowRecoveryEntryPlan plan,
        IWorkflowFaultRecoveryContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new WorkflowRecoveryEntryValidation(
            plan.EntryKey == "OperationStart" && _position == 0 && _feeds == 1,
            "模拟检查要求：位置0、进料次数1、恢复入口OperationStart。"));
    }

    private static WorkflowPortDescriptor[] Ports() => new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() };
    private static void Add(WorkflowDocument document, WorkflowNodeModel node, double x, double y) =>
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = node, X = x, Y = y });
    private static void Connect(WorkflowDocument document, string from, string to, string port = WorkflowPorts.Success) =>
        document.CanvasProjection.Connections.Add(new WorkflowConnectionModel { FromNodeId = from, FromPort = port, ToNodeId = to });

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
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: ++demo._feeds));
        }
    }

    private sealed class MoveHandler(WorkflowRecoveryDemo demo) : WorkflowNodeHandler<MoveNode>, IWorkflowNodeOperationFactory
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(MoveNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("本节点由操作工厂执行。");
        public ValueTask<IWorkflowNodeOperation> CreateOperationAsync(Guid operationId, IWorkflowNodeModel node,
            IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IWorkflowNodeOperation>(new MoveOperation(demo, demo._position + 10));
    }

    private sealed class MoveOperation(WorkflowRecoveryDemo demo, int target) : IWorkflowNodeOperation
    {
        private bool _sent;
        public ValueTask<NodeExecutionResult> ExecuteAsync(IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_sent)
            {
                _sent = true; demo._commands++; demo._position = target;
                if (!demo._lostResponse)
                {
                    demo._lostResponse = true;
                    return ValueTask.FromResult(NodeExecutionResult.Fail("模拟轴完成响应丢失；设备实际状态须由处置逻辑核实。"));
                }
            }
            if (demo._position != target) return ValueTask.FromResult(NodeExecutionResult.Fail("原目标不再满足，不能确认原操作完成。"));
            var result = new MoveResult(demo._position, demo._commands, demo._feeds);
            context.Trace("模拟恢复结果", $"位置={result.Position}，命令次数={result.Commands}，进料次数={result.Feeds}");
            return ValueTask.FromResult(NodeExecutionResult.Continue(output: result));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

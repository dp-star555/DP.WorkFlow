using System.Collections.Concurrent;

namespace DP.WorkFlow.Tests;

/// <summary>第 8 类：流程自带的处理块、读取故障与停点的节点、各去向节点，经统管层故障会话端到端执行。</summary>
public sealed class ProcessFaultHandlingTests
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    [Fact]
    public void WarningHandlerBlock_IsFlowOwnedFaultHandlerWithoutPorts()
    {
        var block = new WarningHandlerBlockNodeModel { Id = "WarningBlock", Title = "告警处理" };
        var descriptor = new WorkflowNodeCatalog().RegisterProcessNodes().GetOrThrow(block.NodeType);

        Assert.IsAssignableFrom<IWorkflowFaultHandlerNode>(block);
        Assert.Equal("WarningHandlerStart", block.SubDocument.EntryNodeId);
        Assert.DoesNotContain(descriptor.Ports, port => port.Direction is WorkflowPortDirection.Input or WorkflowPortDirection.Output);
    }

    [Theory]
    [InlineData("Continue", true, 1, 1)]
    [InlineData("Restart", true, 2, 0)]
    [InlineData("Stop", false, 1, 0)]
    public async Task OwnHandlerBlock_IsPreferredAndItsDirectiveIsApplied(string choice, bool success, int moves, int verifies)
    {
        var (document, block) = MainFlow();
        AddHandler(block,
            new OperatorChoiceNodeModel { Id = "Choice", OptionsText = "继续=Continue;重做=Restart;停止=Stop" },
            ("Continue", new ContinueOperationNodeModel { Id = "Continue", Note = "核实后继续" }),
            ("Restart", new RestartFromEntryNodeModel { Id = "Restart", EntryKey = "Op" }),
            ("Stop", new StopCurrentStationNodeModel { Id = "Stop", Reason = "操作员停止" }));
        var defaultHandler = new WorkflowDocument { Name = "默认", EntryNodeId = "Never" };
        defaultHandler.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new StopCurrentStationNodeModel { Id = "Never" } });
        var operators = new ScriptedOperator(choice);
        using var center = Center(operators, new WorkflowRunCenterOptions { DefaultFaultHandler = defaultHandler, FaultSettleMilliseconds = 0 });

        var result = await center.AddFlow(document, "工位").RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // 默认处理流程会停止流程；继续、回到入口成功说明用的是流程自带的处理块。
        Assert.Equal(success, result.Success);
        Assert.Equal(moves, Count("Move"));
        Assert.Equal(verifies, Count("Move:verify"));
        // 提示标题带出故障的流程名，正文带停点位置和做到哪一步。
        var prompt = Assert.Single(operators.Prompts);
        Assert.StartsWith("[工位]", prompt.Title);
        Assert.Contains("命令已发", prompt.Message);
        if (choice == "Stop")
            Assert.Contains("操作员停止", result.Message);
    }

    [Fact]
    public async Task FaultInfoAndStopPointNodes_DescribeTheStop()
    {
        var (document, block) = MainFlow();
        var stop = new FaultStopPointNodeModel { Id = "ReadStop" };
        AddChain(block, stop, new EndLapNodeModel { Id = "EndLap" });
        WorkflowFaultInfo? info = null;
        WorkflowFaultStopInfo? stopInfo = null;
        using var center = Center(new ScriptedOperator("Continue"), new WorkflowRunCenterOptions { FaultSettleMilliseconds = 0 });
        center.FaultSessionEnded += session => info = WorkflowFaultInfo.From(session);

        var flow = center.AddFlow(document, "工位");
        var result = await flow.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // 主流程没有循环：处理流程给"结束本圈"不被允许，处理流程出错，没有兜底时停止流程。
        Assert.False(result.Success);
        Assert.NotNull(info);
        Assert.Equal("工位", info!.OriginFlow);
        stopInfo = Assert.Single(info.Stops);
        Assert.Equal("Move", stopInfo.NodeId);
        Assert.Equal("故障", stopInfo.Reason);
        Assert.Equal("命令已发", stopInfo.Progress);
        Assert.True(stopInfo.CanContinueOperation);
        Assert.False(stopInfo.CanRedo);
        Assert.False(stopInfo.CanEndLap);
        Assert.Equal(new[] { "Op" }, stopInfo.Entries);
        Assert.Contains("没有可以结束本圈的循环", info.LastFailure);
    }

    [Fact]
    public async Task DirectiveNodeOutsideFaultHandler_Fails()
    {
        var document = new WorkflowDocument { Name = "普通流程", EntryNodeId = "Continue" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new ContinueOperationNodeModel { Id = "Continue" } });
        var catalog = new WorkflowNodeCatalog().RegisterProcessNodes();

        var result = await new WorkflowEngine(new WorkflowCompiler(catalog).Compile(document),
            new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers()).RunAsync();

        Assert.False(result.Success);
        Assert.Contains("只能在故障处理流程", result.Message);
    }

    // ---- 辅助 ----

    private int Count(string key) => _counts.TryGetValue(key, out var value) ? value : 0;

    /// <summary>主流程：恢复入口 Op → 模拟移动（第一次失败，可核实并补完）；另有不连线的处理块。</summary>
    private static (WorkflowDocument Document, WarningHandlerBlockNodeModel Block) MainFlow()
    {
        var document = new WorkflowDocument { Name = "主流程", EntryNodeId = "Entry" };
        var canvas = document.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new RecoveryEntryNodeModel { Id = "Entry", EntryKey = "Op" } });
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = new MoveNode { Id = "Move", Title = "模拟移动" } });
        canvas.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Entry", FromPort = WorkflowPorts.Success, ToNodeId = "Move" });
        var block = new WarningHandlerBlockNodeModel { Id = "Handler", Title = "处理块" };
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = block });
        return (document, block);
    }

    private static void AddHandler(WarningHandlerBlockNodeModel block, OperatorChoiceNodeModel choice,
        params (string Port, WorkflowNodeModel Node)[] branches)
    {
        var sub = block.SubDocument.CanvasProjection;
        sub.Nodes.Clear();
        sub.Connections.Clear();
        sub.Nodes.Add(new WorkflowCanvasNode { Node = new WarningHandlerStartNodeModel { Id = "Start" } });
        sub.Nodes.Add(new WorkflowCanvasNode { Node = choice });
        block.SubDocument.EntryNodeId = "Start";
        sub.Connections.Add(new WorkflowConnectionModel { FromNodeId = "Start", FromPort = WorkflowPorts.Success, ToNodeId = choice.Id });
        foreach (var (port, node) in branches)
        {
            sub.Nodes.Add(new WorkflowCanvasNode { Node = node });
            sub.Connections.Add(new WorkflowConnectionModel { FromNodeId = choice.Id, FromPort = port, ToNodeId = node.Id });
        }
    }

    private static void AddChain(WarningHandlerBlockNodeModel block, params WorkflowNodeModel[] nodes)
    {
        var sub = block.SubDocument.CanvasProjection;
        sub.Nodes.Clear();
        sub.Connections.Clear();
        sub.Nodes.Add(new WorkflowCanvasNode { Node = new WarningHandlerStartNodeModel { Id = "Start" } });
        block.SubDocument.EntryNodeId = "Start";
        var previous = "Start";
        foreach (var node in nodes)
        {
            sub.Nodes.Add(new WorkflowCanvasNode { Node = node });
            sub.Connections.Add(new WorkflowConnectionModel { FromNodeId = previous, FromPort = WorkflowPorts.Success, ToNodeId = node.Id });
            previous = node.Id;
        }
    }

    private WorkflowRunCenter Center(IWorkflowOperatorService operators, WorkflowRunCenterOptions options)
    {
        var catalog = new WorkflowNodeCatalog().RegisterProcessNodes()
            .Register(WorkflowNodeDescriptor.Create<MoveNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
        var handlers = new WorkflowNodeHandlerCatalog().RegisterProcessNodeHandlers().Register(new MoveHandler(_counts));
        var services = new WorkflowServiceProvider().Add(operators);
        return new WorkflowRunCenter(catalog, handlers, options, services);
    }

    [WorkflowNode("Test.Move")]
    private sealed class MoveNode : WorkflowNodeModel
    {
        public override string NodeType => "Test.Move";
    }

    private sealed class MoveHandler(ConcurrentDictionary<string, int> counts) : WorkflowNodeHandler<MoveNode>, IWorkflowContinuableNodeHandler
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(MoveNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var count = counts.AddOrUpdate("Move", 1, (_, value) => value + 1);
            context.ReportProgress("命令已发");
            return ValueTask.FromResult(count == 1 ? NodeExecutionResult.Fail("到位信号丢失") : NodeExecutionResult.Continue());
        }

        public ValueTask<NodeExecutionResult> VerifyAndCompleteAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context,
            WorkflowNodeInterruption interruption, CancellationToken cancellationToken)
        {
            counts.AddOrUpdate("Move:verify", 1, (_, value) => value + 1);
            return ValueTask.FromResult(NodeExecutionResult.Continue());
        }
    }

    private sealed class ScriptedOperator(string choice) : IWorkflowOperatorService
    {
        public List<(string Title, string Message)> Prompts { get; } = new();
        public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken)
        {
            Prompts.Add((title, message));
            return ValueTask.FromResult(choice);
        }
        public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

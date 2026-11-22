namespace DP.WorkFlow.Tests;

/// <summary>R1-01 节点提示：不影响运行结果的非致命信息，按节点累计、跨运行保留，确认后清除。</summary>
public sealed class WorkflowNoticeTests
{
    [Fact]
    public async Task NodeNotice_IsKeptAcrossRuns_MergesRepeats_AndClearsOnAcknowledge()
    {
        var options = new WorkflowExecutionOptions();
        var plan = CreatePlan();

        for (var run = 0; run < 2; run++)
        {
            var result = await new WorkflowEngine(plan, Handlers(), new WorkflowContext(), options).RunAsync();
            Assert.True(result.Success, result.Message);
        }

        var notice = Assert.Single(options.Notices.Get("$", "N"));
        Assert.Equal("设备", notice.Category);
        Assert.Equal("温度偏高，仍在允许范围内。", notice.Message);
        Assert.Equal(2, notice.Count);
        var version = options.Notices.Version;

        Assert.Equal(1, options.Notices.Acknowledge("$", "N"));
        Assert.Empty(options.Notices.GetAll());
        Assert.NotEqual(version, options.Notices.Version);
    }

    [Fact]
    public async Task NodeNotice_IsRecordedAsEvent_AndAdvancesChangeVersion()
    {
        var engine = new WorkflowEngine(CreatePlan(), Handlers(), new WorkflowContext());

        var result = await engine.RunAsync();

        Assert.True(result.Success, result.Message);
        var raised = Assert.Single(engine.GetTraceBatch().Entries, entry => entry.Step == "NodeNoticeRaised");
        Assert.Equal("N", raised.NodeId);
        Assert.Equal("设备", raised.Data!["Category"]);
        Assert.Equal(1, engine.GetRuntimeSnapshot().Sequence); // 提示不构造快照，只递增变化计数。
    }

    [Fact]
    public async Task FailingSubscriber_BecomesCanvasNotice()
    {
        var engine = new WorkflowEngine(CreatePlan(), Handlers(), new WorkflowContext());
        engine.NodeCompleted += _ => throw new InvalidOperationException("界面回调出错");

        var result = await engine.RunAsync();
        await engine.FlushNotificationsAsync();

        Assert.True(result.Success, result.Message);
        var canvas = Assert.Single(engine.Notices.Get("$", null));
        Assert.Equal("记录", canvas.Category);
        Assert.Contains("界面回调出错", canvas.Message);
    }

    [Fact]
    public void Board_DropsOldestBeyondLimit()
    {
        var board = new WorkflowNoticeBoard(maxPerTarget: 2);

        board.Raise(Guid.Empty, "$", "N", "c", "一");
        board.Raise(Guid.Empty, "$", "N", "c", "二");
        board.Raise(Guid.Empty, "$", "N", "c", "三");

        Assert.Equal(new[] { "二", "三" }, board.Get("$", "N").Select(notice => notice.Message));
        Assert.Equal(1, board.DroppedCount);
    }

    private static WorkflowExecutionPlan CreatePlan()
    {
        var node = new NoticeTestNode { Id = "N", Title = "提示" };
        var document = new WorkflowDocument { Name = "提示", EntryNodeId = node.Id };
        document.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return new WorkflowCompiler(new WorkflowNodeCatalog().Register<NoticeTestNode>(1, WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output()))
            .Compile(document);
    }

    private static WorkflowNodeHandlerCatalog Handlers() => new WorkflowNodeHandlerCatalog().Register(new NoticeHandler());

    [WorkflowNode("NoticeTest")]
    private sealed class NoticeTestNode : WorkflowNodeModel
    {
        public override string NodeType => "NoticeTest";
    }

    private sealed class NoticeHandler : WorkflowNodeHandler<NoticeTestNode>
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(
            NoticeTestNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            context.RaiseNotice("设备", "温度偏高，仍在允许范围内。");
            return ValueTask.FromResult(NodeExecutionResult.Complete());
        }
    }
}

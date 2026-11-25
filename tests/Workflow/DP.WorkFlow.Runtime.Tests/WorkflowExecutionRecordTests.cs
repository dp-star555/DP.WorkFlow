using System.Collections.Concurrent;
using static DP.WorkFlow.Tests.WorkflowStopPointTests;

namespace DP.WorkFlow.Tests;

/// <summary>第 8 类：合并执行记录、逆序回退、恢复轮次号与"恢复时清除"的公共数据。</summary>
public sealed class WorkflowExecutionRecordTests
{
    [Fact]
    public async Task Undo_RunsInReverseOrderAcrossFlowsAndNeverTwice()
    {
        var record = new WorkflowExecutionRecord();
        var undone = new List<string>();
        Add(record, "A", "A1", undone);
        Add(record, "B", "B1", undone);
        Add(record, "A", "A2", undone);
        Add(record, "A", "Read", null, sideEffectFree: true);
        Add(record, "B", "B2", undone);

        var report = await record.UndoAsync(new[] { new WorkflowUndoTarget("A", "A1"), new WorkflowUndoTarget("B", null) }, null, CancellationToken.None);

        Assert.True(report.Success, report.Failure);
        Assert.Equal(new[] { "B2", "A2", "B1" }, undone);
        Assert.Equal(new[] { "B2", "Read", "A2", "B1" }, report.Steps.Select(step => step.Entry.NodeId));
        Assert.All(report.Steps, step => Assert.Equal(WorkflowUndoStepKind.Undone, step.Kind));
        // 重新运行处理流程：已回退的不再回退。
        var again = await record.UndoAsync(new[] { new WorkflowUndoTarget("A", "A1"), new WorkflowUndoTarget("B", null) }, null, CancellationToken.None);
        Assert.True(again.Success);
        Assert.Empty(again.Steps);
        Assert.Equal(3, undone.Count);
    }

    [Fact]
    public async Task Undo_NodeWithoutUndoNeedsConfirmation()
    {
        var record = new WorkflowExecutionRecord();
        Add(record, "A", "Pick", null);

        var refused = await record.UndoAsync(new[] { new WorkflowUndoTarget("A", null) }, null, CancellationToken.None);
        Assert.False(refused.Success);
        Assert.Contains("需要人工处理", refused.Failure);

        var confirmed = await record.UndoAsync(new[] { new WorkflowUndoTarget("A", null) }, (_, _) => Task.FromResult(true), CancellationToken.None);
        Assert.True(confirmed.Success);
        Assert.Equal(WorkflowUndoStepKind.Confirmed, Assert.Single(confirmed.Steps).Kind);
    }

    [Fact]
    public async Task Undo_UnknownBackToNodeFails()
    {
        var record = new WorkflowExecutionRecord();
        Add(record, "A", "A1", new List<string>());

        var report = await record.UndoAsync(new[] { new WorkflowUndoTarget("A", "Nope") }, null, CancellationToken.None);

        Assert.False(report.Success);
        Assert.Contains("Nope", report.Failure);
    }

    [Fact]
    public void Checkpoint_DropsEarlierEntriesOfSameFlowAndCapacityDropsOldest()
    {
        var record = new WorkflowExecutionRecord(capacity: 3);
        Add(record, "A", "A1", null);
        Add(record, "B", "B1", null);
        record.Add("A", 0, new WorkflowJournalEntry("Hand", "Hand", "T", "$/Hand", true, null));
        Assert.Equal(new[] { "B1", "Hand" }, record.Entries.Select(entry => entry.NodeId));

        Add(record, "A", "A2", null);
        Add(record, "A", "A3", null);
        Assert.Equal(new[] { "Hand", "A2", "A3" }, record.Entries.Select(entry => entry.NodeId));
    }

    [Fact]
    public async Task FaultSession_HandlerUndoesActionsThenReleaseAdvancesRoundAndResetsPublicData()
    {
        var probe = new Probe();
        var undone = new ConcurrentQueue<string>();
        var flow = new TestFlow(probe);
        flow.Add(new LiftNode { Id = "Down" });
        flow.Add(new LiftNode { Id = "Grip" });
        flow.Add(new StepNode { Id = "Read" });
        flow.Add(new ActNode { Id = "Place", FailTimes = 1 });
        flow.Connect("Down", "Grip");
        flow.Connect("Grip", "Read");
        flow.Connect("Read", "Place");
        var handler = new WorkflowDocument { Name = "处理流程", EntryNodeId = "Undo" };
        handler.Nodes.Add(new WorkflowCanvasNode { Node = new UndoAllNode { Id = "Undo" } });
        var options = new WorkflowRunCenterOptions { DefaultFaultHandler = handler, FaultSettleMilliseconds = 0 };
        var catalog = TestFlow.Catalog()
            .Register(WorkflowNodeDescriptor.Create<LiftNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }))
            .Register(WorkflowNodeDescriptor.Create<UndoAllNode>(ports: new[] { WorkflowPortDescriptor.Input(), WorkflowPortDescriptor.Output() }));
        var handlers = flow.Handlers().Register(new LiftHandler(undone)).Register(new UndoAllHandler());
        using var center = new WorkflowRunCenter(catalog, handlers, options);
        center.PublicData.Declare(new WorkflowPublicDataDeclaration("Handshake", WorkflowPublicDataKind.Signal, E_SignalValueType.Boolean, ResetOnRecovery: true));
        center.PublicData.Declare(WorkflowPublicDataDeclaration.Signal("Keep"));
        center.PublicData.Signals.Write("Handshake", true);
        center.PublicData.Signals.Write("Keep", true);
        var centerFlow = center.AddFlow(flow.Document, "A");
        Assert.Equal(0, centerFlow.RecoveryRound);

        var result = await centerFlow.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "Grip", "Down" }, undone);
        Assert.Equal(1, probe.Count("Place:verify"));
        Assert.Equal(1, centerFlow.RecoveryRound);
        Assert.False(center.PublicData.Signals.Read("Handshake"));
        Assert.True(center.PublicData.Signals.Read("Keep"));
        // 回退过的记录留着标记，恢复后新执行的节点带新的轮次号。
        var entries = center.ExecutionRecord.EntriesOf("A");
        Assert.All(entries.Where(entry => entry.Round == 0), entry => Assert.True(entry.Undone));
        Assert.Contains(entries, entry => entry.NodeId == "Place" && entry.Round == 1 && !entry.Undone);
    }

    private static void Add(WorkflowExecutionRecord record, string flow, string node, List<string>? undone,
        bool sideEffectFree = false) =>
        record.Add(flow, 0, new WorkflowJournalEntry(node, node, "T", "$/" + node, false,
            sideEffectFree ? _ => Task.CompletedTask
            : undone is null ? null : _ => { undone.Add(node); return Task.CompletedTask; }));

    /// <summary>支持回退的节点（例如下降 / 夹紧）。</summary>
    [WorkflowNode("T.Lift")]
    private sealed class LiftNode : WorkflowNodeModel
    {
        public override string NodeType => "T.Lift";
    }

    private sealed class LiftHandler(ConcurrentQueue<string> undone) : WorkflowNodeHandler<LiftNode>, IWorkflowUndoableNodeHandler
    {
        protected override ValueTask<NodeExecutionResult> ExecuteAsync(LiftNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NodeExecutionResult.Continue());

        public ValueTask UndoAsync(IWorkflowNodeModel node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            undone.Enqueue(node.Id);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>处理流程：回退出故障流程的全部动作，然后就地恢复。</summary>
    [WorkflowNode("T.UndoAll")]
    private sealed class UndoAllNode : WorkflowNodeModel
    {
        public override string NodeType => "T.UndoAll";
    }

    private sealed class UndoAllHandler : WorkflowNodeHandler<UndoAllNode>
    {
        protected override async ValueTask<NodeExecutionResult> ExecuteAsync(UndoAllNode node, IWorkflowNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var session = (WorkflowFaultSession)context.Services.GetService(typeof(WorkflowFaultSession))!;
            var report = await session.Center.ExecutionRecord.UndoAsync(new[] { new WorkflowUndoTarget(session.OriginFlow!, null) }, null, cancellationToken);
            if (!report.Success)
                return NodeExecutionResult.Fail(report.Failure!);
            session.SetDirective(null, WorkflowResumeDirective.RecoverInPlace());
            return NodeExecutionResult.Continue();
        }
    }
}

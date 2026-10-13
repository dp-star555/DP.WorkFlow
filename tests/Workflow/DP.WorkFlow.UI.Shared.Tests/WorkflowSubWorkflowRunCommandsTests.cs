using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowSubWorkflowRunCommandsTests
{
    [Fact]
    public async Task RunCommands_RunTheCompositeNodeOfTheDraftCanvas_OnlyWhenTheWorkflowIsIdleAndHasRun()
    {
        var (session, loop) = RootSession();
        var runtime = new FakeRuntime();
        session.Runtime = runtime;
        await using var model = new WorkflowNodeEditorModel(session, loop.Id, loop.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        using var commands = new WorkflowSubWorkflowRunCommands(page);

        // 子画布继承所在画布的运行入口，并记下自己在运行中的位置。
        Assert.Same(runtime, page.Session.Runtime);
        Assert.Equal(new[] { loop.Id }, page.Session.RuntimePath);
        Assert.False(commands.CanRun);
        Assert.Contains("先运行一次主流程", commands.RunToolTip);

        runtime.HasRun = true;
        runtime.State = E_WorkflowExecutionState.Running;
        Assert.False(commands.CanRun);
        Assert.Contains("整个流程", commands.RunToolTip);

        runtime.State = E_WorkflowExecutionState.Completed;
        Assert.True(commands.CanRun);
        var result = await commands.RunAsync();

        Assert.True(result.Success);
        var (document, nodeId) = Assert.Single(runtime.RunNodeCalls);
        Assert.Equal(loop.Id, nodeId);
        // 执行的是窗口的编辑副本（含未确认的修改），不是正式文档。
        Assert.Same(model.EditingSession.Document, document);
        commands.Pause();
        commands.Resume();
        commands.Cancel();
        Assert.Equal(new[] { "Pause", "Resume", "Cancel" }, runtime.Controls);
    }

    [Fact]
    public async Task RunCommands_NestedCompositeCannotRunOnItsOwn()
    {
        var (session, loop) = RootSession();
        session.Runtime = new FakeRuntime { HasRun = true, State = E_WorkflowExecutionState.Completed };
        session.RuntimePath = new[] { "Outer" };
        await using var model = new WorkflowNodeEditorModel(session, loop.Id, loop.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        using var commands = new WorkflowSubWorkflowRunCommands(page);

        Assert.Equal(new[] { "Outer", loop.Id }, page.Session.RuntimePath);
        Assert.False(commands.CanRun);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.RunAsync());
    }

    [Fact]
    public void TraceScope_KeepsOnlyTheSubCanvasEntries_WithTheCycleInFront()
    {
        var now = DateTimeOffset.UtcNow;
        var batch = new WorkflowTraceBatch(Guid.NewGuid(), 3, new[]
        {
            new WorkflowTraceEntry(1, now, "Loop", "LoopBlock", "NodeStarted", null, null),
            new WorkflowTraceEntry(1, now, "Work", "Action", "NodeCompleted", null, null) { Parents = new[] { new WorkflowTraceParent("Loop", 2) } },
            new WorkflowTraceEntry(2, now, "Inner", "Action", "NodeCompleted", null, null)
            {
                Parents = new[] { new WorkflowTraceParent("Loop", 2), new WorkflowTraceParent("Block") }
            },
            new WorkflowTraceEntry(1, now, "Other", "Action", "NodeCompleted", null, null) { Parents = new[] { new WorkflowTraceParent("Block") } }
        });

        var scoped = WorkflowTraceScope.ForPath(batch, new[] { "Loop" })!;

        Assert.Equal(new[] { "第2圈 › Work", "第2圈 › Block › Inner" }, scoped.Entries.Select(WorkflowRuntimeMonitorModel.DescribeNode));
        Assert.Same(batch, WorkflowTraceScope.ForPath(batch, Array.Empty<string>()));
    }

    [Fact]
    public async Task SessionMonitorSource_FollowsTheSubCanvasRun()
    {
        var (session, loop) = RootSession();
        var runtime = new FakeRuntime
        {
            Trace = new WorkflowTraceBatch(Guid.NewGuid(), 1, new[]
            {
                new WorkflowTraceEntry(1, DateTimeOffset.UtcNow, "Work", "Action", "NodeCompleted", null, null)
                {
                    Parents = new[] { new WorkflowTraceParent("Loop", 1) }
                }
            })
        };
        session.Runtime = runtime;
        await using var model = new WorkflowNodeEditorModel(session, loop.Id, loop.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        using var source = new WorkflowSessionRuntimeMonitorSource(page.Session);
        var changes = 0;
        source.Changed += (_, _) => changes++;

        runtime.Raise();
        Assert.Equal(1, changes);
        Assert.Equal("第1圈 › Work", WorkflowRuntimeMonitorModel.DescribeNode(Assert.Single(source.GetTraceBatch()!.Entries)));
        Assert.Null(source.GetSnapshot());
    }

    private static (WorkflowDesignerSession Session, LoopBlockNodeModel Loop) RootSession()
    {
        var child = new WorkflowDocument { Name = "Child", EntryNodeId = "ChildStart" };
        child.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "ChildStart" } });
        var loop = new LoopBlockNodeModel { Id = "Loop", Title = "循环", SubDocument = child };
        var document = new WorkflowDocument { Name = "Root" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = loop });
        return (new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes()), loop);
    }

    private sealed class FakeRuntime : IWorkflowDesignerRuntime
    {
        public E_WorkflowExecutionState State { get; set; }
        public bool HasRun { get; set; }
        public WorkflowTraceBatch? Trace { get; set; }
        public List<(WorkflowDocument Document, string NodeId)> RunNodeCalls { get; } = new();
        public List<string> Controls { get; } = new();
        public event EventHandler? StateChanged;
        public void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
        public WorkflowTraceBatch? GetTraceBatch(WorkflowTraceCursor? since = null) => Trace;

        public Task<WorkflowRunResult> RunNodeAsync(WorkflowDocument document, string nodeId, CancellationToken cancellationToken = default)
        {
            RunNodeCalls.Add((document, nodeId));
            return Task.FromResult(new WorkflowRunResult(true, E_WorkflowExecutionState.Completed, TimeSpan.Zero, "ok"));
        }

        public void Pause() => Controls.Add("Pause");
        public void Resume() => Controls.Add("Resume");
        public void Cancel() => Controls.Add("Cancel");
    }
}

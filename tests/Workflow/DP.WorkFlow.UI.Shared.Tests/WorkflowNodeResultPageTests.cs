using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeResultPageTests
{
    [Fact]
    public async Task EveryNodeEditor_GetsResultsPage_ExceptDedicatedPropertyEditors()
    {
        var (session, start) = CreateSession();

        await using var model = new WorkflowNodeEditorModel(session, start.Id, start.Id);

        var page = Assert.Single(model.Pages, item => item.PageId == WorkflowNodeResultPageProvider.PageId);
        Assert.Equal(WorkflowNodeEditorPageKind.Results, page.Kind);
        var results = Assert.IsType<WorkflowNodeResultPageModel>(page.Model);
        Assert.Equal(new[] { new WorkflowNodeResultItem(WorkflowNodeResultPageModel.StateCategory, "状态", "尚未运行") }, results.GetItems());
        Assert.False(new WorkflowNodeResultPageProvider().CanProvide(
            new WorkflowNodeEditorContext(session, start.Id, start) { RequestedPropertyEditor = "Editor" }));
    }

    [Fact]
    public async Task AfterRun_ShowsStateAndLatestOutputFromRuntimeSession()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes();
        var start = new StartNodeModel { Id = "Start", Title = "开始" };
        var document = new WorkflowDocument { Name = "Run" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = start });
        document.EntryNodeId = start.Id;
        var navigator = new WorkflowDesignerNavigator(document, catalog);
        using var host = new WorkflowRuntimeHost(catalog, new WorkflowNodeHandlerCatalog().Register(new StartNodeHandler()));
        using var binding = new WorkflowStudioRuntimeBinding(host, navigator) { AutoConfigureBeforeRun = true };

        var result = await binding.RunAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(navigator.RootSession.GetLatestNodeOutput(start.Id));
        await using var model = new WorkflowNodeEditorModel(navigator.RootSession, start.Id, start.Id);
        var items = Assert.IsType<WorkflowNodeResultPageModel>(
            model.Pages.Single(page => page.PageId == WorkflowNodeResultPageProvider.PageId).Model).GetItems();
        Assert.Contains(new WorkflowNodeResultItem(WorkflowNodeResultPageModel.StateCategory, "状态", "已完成"), items);
        Assert.Contains(new WorkflowNodeResultItem(WorkflowNodeResultPageModel.StateCategory, "执行次数", "1"), items);
        Assert.Contains(items, item => item.Category == WorkflowNodeResultPageModel.OutputCategory
            && item.Value != "无输出记录");
    }

    [Fact]
    public void LatestOutput_IsIgnoredWhenItBelongsToAnotherRun()
    {
        var (session, start) = CreateSession();
        var runId = Guid.NewGuid();
        session.SetRuntimeSnapshot(Snapshot(runId, start.Id));
        var output = new WorkflowNodeOutput(1, Guid.NewGuid(), start.Id, 1, 1, Array.Empty<long>(), DateTimeOffset.UtcNow, 42);
        session.NodeOutputProvider = _ => output;

        Assert.Null(session.GetLatestNodeOutput(start.Id));
        using var results = new WorkflowNodeResultPageModel(session, start.Id);
        Assert.Contains(new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, "输出", "无输出记录"), results.GetItems());

        session.NodeOutputProvider = _ => output with { RunId = runId };
        Assert.Contains(new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, "值", "42"), results.GetItems());
    }

    [Fact]
    public void Output_ExpandsPublicPropertiesAndFormatsValues()
    {
        var items = new List<WorkflowNodeResultItem>();

        WorkflowNodeResultPageModel.AddOutput(items, new SampleOutput());

        Assert.Equal(
            new[]
            {
                new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, nameof(SampleOutput.Count), "3"),
                new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, nameof(SampleOutput.Passed), "True"),
                new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, nameof(SampleOutput.Items), "2 项"),
                new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, nameof(SampleOutput.Missing), "（空）"),
                new WorkflowNodeResultItem(WorkflowNodeResultPageModel.OutputCategory, nameof(SampleOutput.Broken), "读取失败：broken")
            },
            items);
        Assert.Equal("1.5", WorkflowNodeResultPageModel.FormatValue(1.5));
        Assert.Equal(241, WorkflowNodeResultPageModel.FormatValue(new string('x', 500)).Length);
        Assert.Equal("a b", WorkflowNodeResultPageModel.FormatValue("a\nb"));
    }

    [Fact]
    public void RuntimeSnapshotChanges_RaiseChangedUntilDisposed()
    {
        var (session, start) = CreateSession();
        var results = new WorkflowNodeResultPageModel(session, start.Id);
        var changes = 0;
        results.Changed += (_, _) => changes++;

        session.SetRuntimeSnapshot(Snapshot(Guid.NewGuid(), start.Id));
        session.SelectNodes(Array.Empty<string>());
        results.Dispose();
        session.SetRuntimeSnapshot(null);

        Assert.Equal(1, changes);
    }

    private static (WorkflowDesignerSession Session, StartNodeModel Start) CreateSession()
    {
        var start = new StartNodeModel { Id = "Start", Title = "开始" };
        var document = new WorkflowDocument { Name = "Results" };
        document.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = start });
        document.EntryNodeId = start.Id;
        return (new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes()), start);
    }

    private static WorkflowRuntimeSnapshot Snapshot(Guid runId, string nodeId) => new(
        runId, 1, DateTimeOffset.UtcNow, "Results", E_WorkflowExecutionState.Completed, null,
        Array.Empty<string>(), new Dictionary<long, WorkflowActiveTokenInfo>(), TimeSpan.Zero,
        new Dictionary<string, WorkflowNodeRuntimeInfo>
        {
            [nodeId] = new(nodeId, E_NodeState.Completed, 1, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(2))
        },
        new Dictionary<long, WorkflowParallelScopeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        Array.Empty<string>());

    private sealed class SampleOutput
    {
        public int Count => 3;
        public bool Passed => true;
        public IReadOnlyList<int> Items { get; } = new[] { 1, 2 };
        public string? Missing => null;
        public string Broken => throw new InvalidOperationException("broken");
    }
}

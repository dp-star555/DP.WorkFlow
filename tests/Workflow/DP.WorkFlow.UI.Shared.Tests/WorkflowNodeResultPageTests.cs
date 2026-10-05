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
    public void Output_UsesDisplayNameAttributesAndRegisteredNames()
    {
        WorkflowOutputDisplayNames.Register(typeof(RegisteredOutput), new Dictionary<string, string> { ["Score"] = "分数" });
        var items = new List<WorkflowNodeResultItem>();

        WorkflowNodeResultPageModel.AddOutput(items, new AttributedOutput(true, 7));
        WorkflowNodeResultPageModel.AddOutput(items, new RegisteredOutput());

        Assert.Equal(new[] { "是否成功", "Code", "分数" }, items.Select(item => item.Name));
    }

    [Fact]
    public void BuiltInNodeOutputs_HaveChineseNamesForEveryMember()
    {
        var catalog = new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes().RegisterImageNodes();

        Assert.Empty(catalog.Snapshot().Values.SelectMany(descriptor => UnnamedMembers(descriptor.OutputType)).Distinct());
        Assert.Equal("匹配分数", WorkflowOutputDisplayNames.Resolve(typeof(DP.Vision.Algorithms.TemplatePoseResult).GetProperty("Score")!));
    }

    [Fact]
    public void TemplateMatchOutput_ShowsPoseValues_AndHidesDiagnosticMembers()
    {
        WorkflowVisionOutputNames.EnsureRegistered();
        var result = new DP.Vision.Algorithms.TemplatePoseResult("frame", "template", .95,
            new DP.Vision.Algorithms.TemplatePoseTransform(20, 10, new DP.Vision.PointD(50, 40), Math.PI / 2, 1.5),
            new DP.Vision.Algorithms.TemplateReference(10, 5, 0, "reference"));
        var items = new List<WorkflowNodeResultItem>();

        WorkflowNodeResultPageModel.AddOutput(items, result);

        Assert.Equal(new[] { "是否找到", "匹配分数", "中心X", "中心Y", "角度(°)", "缩放", "参考点X", "参考点Y", "参考方向(°)", "摘要" }, items.Select(item => item.Name));
        Assert.Equal("90", items.Single(item => item.Name == "角度(°)").Value);
    }

    /// <summary>列出输出类型中没有中文显示名称的公开成员；坐标分量 X/Y 本身即为显示名。</summary>
    internal static IEnumerable<string> UnnamedMembers(Type? type)
    {
        if (type is null || type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return Array.Empty<string>();
        return type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0 && property.Name is not ("X" or "Y" or "EqualityContract"))
            .Where(property => WorkflowOutputDisplayNames.Resolve(property) == property.Name)
            .Select(property => $"{type.Name}.{property.Name}");
    }

    [Fact]
    public async Task OutputPorts_AreTogglableOnEditingCopyAndCommittedWithNode()
    {
        var (session, start) = CreateSession();
        var decision = session.AddNode("Decision", 200, 0);

        await using var editor = new WorkflowNodeEditorModel(session, start.Id, decision.Node.Id);
        var results = Assert.IsType<WorkflowNodeResultPageModel>(
            Assert.Single(editor.Pages, page => page.PageId == WorkflowNodeResultPageProvider.PageId).Model);
        var ports = results.GetOutputPorts();
        Assert.True(ports.Count > 1);
        Assert.All(ports, port => Assert.True(port.Visible));
        var changes = 0;
        results.Changed += (_, _) => changes++;

        Assert.True(results.SetOutputPortVisible(ports[1].Key, false));
        Assert.False(results.SetOutputPortVisible(ports[1].Key, false));
        Assert.False(results.GetOutputPorts()[1].Visible);
        Assert.Equal(1, changes);
        // 编辑副本上的改动在“应用/确定”前不影响正式文档。
        Assert.DoesNotContain(ports[1].Key, decision.HiddenOutputPorts);

        editor.ApplyChanges();
        Assert.Contains(ports[1].Key, decision.HiddenOutputPorts);

        Assert.Empty(new WorkflowNodeResultPageModel(session, start.Id, session).GetOutputPorts());
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

    private sealed record AttributedOutput([property: System.ComponentModel.DisplayName("是否成功")] bool Success, int Code);

    private sealed class RegisteredOutput
    {
        public double Score => 0.5;
    }
}

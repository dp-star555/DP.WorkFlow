using DP.WorkFlow.UI;

namespace DP.WorkFlow.Tests;

public sealed class WorkflowNodeEditorModelTests
{
    [Fact]
    public async Task ScriptNode_AutomaticallyGetsPropertiesScriptAndDiagnosticsPages()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本" };
        var document = Document(node);
        var session = new WorkflowDesignerSession(document, new WorkflowNodeCatalog().RegisterStandardNodes());

        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id);

        Assert.Equal(new[] { "Properties", "Script", "ScriptDiagnostics", "Results" }, model.Pages.Select(page => page.PageId));
        var script = Assert.IsType<WorkflowScriptEditorPageModel>(model.Pages.Single(page => page.PageId == "Script").Model);
        var sessionRefreshes = 0;
        model.EditingSession.Changed += (_, _) => sessionRefreshes++;
        var changedSource = WorkflowCSharpScriptEditorModel.EnsureProgramSource("return 42;");
        script.SetScript(changedSource);
        Assert.Empty(script.Compile());
        Assert.Equal(0, sessionRefreshes);
        Assert.Equal(WorkflowCSharpProgramSource.DefaultSource, node.Script);
        Assert.Equal(changedSource, ((IWorkflowScriptNode)model.EditingNode).Script);
        Assert.Empty(script.GetDiagnostics());
        model.ApplyChanges();
        Assert.Equal(changedSource, node.Script);
        Assert.True(session.Undo());
        Assert.Equal(WorkflowCSharpProgramSource.DefaultSource, node.Script);
        Assert.True(session.Redo());
        Assert.Equal(changedSource, node.Script);
    }

    [Fact]
    public async Task PropertyEditorApply_CommitsDirectlyToNodeAndParentDraft()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "原始标题" };
        var session = new WorkflowDesignerSession(Document(block), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());
        await using var parent = new WorkflowNodeEditorModel(session, block.Id, block.Id);
        parent.EditingNode.Title = "节点窗口草稿";
        await using (var child = parent.CreatePropertyEditor("custom"))
        {
            Assert.Equal("节点窗口草稿", child.EditingNode.Title);
            child.EditingNode.Title = "属性窗口修改";
            child.ApplyChanges();
        }
        // 属性窗口应用即提交到正式节点，不必再应用节点窗口；节点窗口草稿同步。
        Assert.Equal("属性窗口修改", block.Title);
        Assert.Equal("属性窗口修改", parent.EditingNode.Title);
        Assert.True(session.Undo());
        Assert.Equal("原始标题", block.Title);
    }

    [Fact]
    public async Task ScriptChanges_CannotBeAppliedBeforeManualCompilation()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id);
        var script = Assert.IsType<WorkflowScriptEditorPageModel>(model.Pages.Single(page => page.PageId == "Script").Model);
        script.SetScript(WorkflowCSharpScriptEditorModel.EnsureProgramSource("return 1;"));

        var exception = Assert.Throws<InvalidOperationException>(() => model.ApplyChanges());

        Assert.Contains("编译", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposingWithoutApply_DiscardsStagedParameterChanges()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "原始标题", Script = "return 1;" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var model = new WorkflowNodeEditorModel(session, node.Id, node.Id);
        model.EditingNode.Title = "临时标题";
        ((IWorkflowScriptNode)model.EditingNode).Script = "return 2;";

        await model.DisposeAsync();

        Assert.Equal("原始标题", node.Title);
        Assert.Equal("return 1;", node.Script);
    }

    [Fact]
    public async Task EmptyBlock_OpeningEditorDoesNotCreateEntryOrMutateOriginalDocument()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "子流程" };
        var session = new WorkflowDesignerSession(Document(block), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());

        await using var model = new WorkflowNodeEditorModel(session, block.Id, block.Id);

        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.Empty(block.SubDocument.EntryNodeId);
        Assert.Empty(page.Session.Document.Graph.Nodes);
        Assert.NotSame(block.SubDocument, page.Session.Document);
    }

    [Fact]
    public async Task BlockSubDocument_IsStagedAndAppliedAsOneUndoableConfigurationTransaction()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "子流程" };
        var session = new WorkflowDesignerSession(Document(block), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());
        await using var model = new WorkflowNodeEditorModel(session, block.Id, block.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);

        var start = page.Session.AddNode("Start", 72, 72);
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.Equal(start.Node.Id, page.Session.Document.EntryNodeId);

        model.ApplyChanges();
        Assert.Equal(start.Node.Id, block.SubDocument.EntryNodeId);
        Assert.Single(block.SubDocument.Graph.Nodes);
        Assert.True(session.Undo());
        Assert.Empty(block.SubDocument.Graph.Nodes);
        Assert.True(session.Redo());
        Assert.Single(block.SubDocument.Graph.Nodes);
    }

    [Fact]
    public async Task SubWorkflowNodeEditor_OpensNodeAddedToChildCanvasAndAppliesIntoParentDraft()
    {
        var block = new BlockNodeModel { Id = "Block", Title = "子流程" };
        var session = new WorkflowDesignerSession(Document(block), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());
        await using var model = new WorkflowNodeEditorModel(session, block.Id, block.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        // 空子流程打开后再放入开始节点和新节点：入口只出现在子会话文档上。
        var start = page.Session.AddNode("Start", 72, 72);
        var added = page.Session.AddNode("Action", 300, 72);

        await using (var child = model.CreateSubWorkflowNodeEditor(page, added.Node.Id))
        {
            Assert.Equal(start.Node.Id, child.EntryNodeId);
            Assert.Contains(child.Pages, item => item.PageId == "Properties");
            child.EditingNode.Title = "子节点修改";
            child.ApplyChanges();
        }

        // 下一层应用只写回本窗口草稿，正式节点要等本窗口应用。
        Assert.Equal("子节点修改", added.Node.Title);
        Assert.Empty(block.SubDocument.Graph.Nodes);
        model.ApplyChanges();
        Assert.Contains(block.SubDocument.Graph.Nodes, item => item.Id == added.Node.Id && item.Title == "子节点修改");
    }

    [Fact]
    public async Task HigherPriorityPluginPage_ReplacesBuiltInCapabilityPageBySlot()
    {
        var node = new CSharpScriptNodeModel { Id = "Script", Title = "脚本" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var provider = new SlotProvider("Vision.Roi", "Script", priority: 100);

        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id, new[] { provider });

        var script = Assert.Single(model.Pages, page => page.PageId == "Script");
        Assert.Equal("Vision.Roi", script.RendererKey);
        Assert.Equal(100, script.Priority);
        Assert.Single(model.Pages, page => page.PageId == "ScriptDiagnostics");
    }

    [Fact]
    public void PageCatalog_RejectsDuplicateExtensionIdBeforeOpeningEditor()
    {
        var catalog = new WorkflowNodeEditorPageCatalog()
            .Register(new SlotProvider("Duplicate", "First", priority: 0));

        var error = Assert.Throws<InvalidOperationException>(() =>
            catalog.Register(new SlotProvider("Duplicate", "Second", priority: 0)));

        Assert.Contains("已经注册", error.Message);
    }

    [Fact]
    public void PageCatalog_RejectsAmbiguousPageSlotAtSamePriority()
    {
        var node = new PlainNode { Id = "Plain", Title = "普通" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var context = new WorkflowNodeEditorContext(session, node.Id, node);
        var catalog = new WorkflowNodeEditorPageCatalog()
            .Register(new SlotProvider("First", "Shared", priority: 10))
            .Register(new SlotProvider("Second", "Shared", priority: 10));

        var error = Assert.Throws<InvalidOperationException>(() => catalog.CreatePages(context));

        Assert.Contains("多个扩展", error.Message);
    }

    [Fact]
    public void PageCatalog_RejectsCustomPageWithoutExplicitRendererKey()
    {
        var node = new PlainNode { Id = "Custom", Title = "扩展" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());
        var context = new WorkflowNodeEditorContext(session, node.Id, node);
        var catalog = new WorkflowNodeEditorPageCatalog().Register(new MissingRendererKeyProvider());

        var error = Assert.Throws<InvalidOperationException>(() => catalog.CreatePages(context));

        Assert.Contains("RendererKey", error.Message);
    }

    [Fact]
    public async Task HostProvider_CanAppendCustomPageWithoutDesktopControlDependency()
    {
        var node = new PlainNode { Id = "Custom", Title = "扩展" };
        var session = new WorkflowDesignerSession(Document(node), new WorkflowNodeCatalog().RegisterStandardNodes());

        await using var model = new WorkflowNodeEditorModel(session, node.Id, node.Id, new[] { new CustomProvider() });

        var page = model.Pages.Single(item => item.PageId == "HostCustom");
        var custom = Assert.IsType<DemoPageModel>(page.Model);
        Assert.Equal(node.Id, custom.NodeId);
        Assert.Equal("DemoRenderer", page.RendererKey);
    }

    [Fact]
    public async Task SubWorkflowPage_FollowsTheCompositeNodesLiveChildRun()
    {
        var work = new ActionNodeModel { Id = "Work", Title = "扫描", FunctionKey = "Work" };
        var child = new WorkflowDocument { Name = "Child" };
        child.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = new StartNodeModel { Id = "ChildStart" } });
        child.CanvasProjection.Nodes.Add(new WorkflowCanvasNode { Node = work });
        child.EntryNodeId = "ChildStart";
        var loop = new BlockNodeModel { Id = "Loop", Title = "循环", SubDocument = child };
        var session = new WorkflowDesignerSession(Document(loop), new WorkflowNodeCatalog().RegisterStandardNodes().RegisterCompositeNodes());
        var childRunId = Guid.NewGuid();
        session.RunNodeOutputProvider = (runId, nodeId) => runId == childRunId && nodeId == "Work"
            ? new WorkflowNodeOutput(1, childRunId, "Work", 1, 1, Array.Empty<long>(), DateTimeOffset.UtcNow, 7)
            : null;
        await using var model = new WorkflowNodeEditorModel(session, loop.Id, loop.Id);
        var page = Assert.IsType<WorkflowSubWorkflowEditorPageModel>(model.Pages.Single(item => item.PageId == "SubWorkflow").Model);
        Assert.Null(page.Session.RuntimeSnapshot);

        // 父画布收到快照（循环第2圈，子流程正在执行 Work）：编辑窗口的子画布随之显示子运行状态。
        session.SetRuntimeSnapshot(RootWithChild(loop.Id, ChildSnapshot(childRunId, "Work"), cycle: 2));

        Assert.Equal(childRunId, page.Session.RuntimeSnapshot?.RunId);
        Assert.Equal(E_NodeState.Running, page.Session.GetNodeState("Work"));
        Assert.Equal(7, page.Session.GetLatestNodeOutput("Work")?.Value);
        await using (var inner = model.CreateSubWorkflowNodeEditor(page, "Work"))
        {
            var results = Assert.IsType<WorkflowNodeResultPageModel>(inner.Pages.Single(item => item.PageId == WorkflowNodeResultPageProvider.PageId).Model);
            Assert.Contains(results.GetItems(), item => item.Name == "状态" && item.Value != "尚未运行");
        }
        var loopResults = Assert.IsType<WorkflowNodeResultPageModel>(model.Pages.Single(item => item.PageId == WorkflowNodeResultPageProvider.PageId).Model);
        var items = loopResults.GetItems();
        Assert.Contains(new WorkflowNodeResultItem(WorkflowNodeResultPageModel.StateCategory, "当前圈", "2"), items);
        Assert.Contains(new WorkflowNodeResultItem(WorkflowNodeResultPageModel.StateCategory, "正在执行", "Work"), items);
    }

    private static WorkflowRuntimeSnapshot ChildSnapshot(Guid runId, string runningNodeId) => new(
        runId, 5, DateTimeOffset.UtcNow, "Child", E_WorkflowExecutionState.Running, runningNodeId, new[] { runningNodeId },
        new Dictionary<long, WorkflowActiveTokenInfo>(), TimeSpan.FromMilliseconds(30),
        new Dictionary<string, WorkflowNodeRuntimeInfo>
        {
            [runningNodeId] = new(runningNodeId, E_NodeState.Running, 1, 1, DateTimeOffset.UtcNow, null, TimeSpan.FromMilliseconds(12))
        },
        new Dictionary<long, WorkflowParallelScopeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        Array.Empty<string>());

    private static WorkflowRuntimeSnapshot RootWithChild(string parentNodeId, WorkflowRuntimeSnapshot child, int cycle) => new(
        Guid.NewGuid(), 9, DateTimeOffset.UtcNow, "Root", E_WorkflowExecutionState.Running, parentNodeId, new[] { parentNodeId },
        new Dictionary<long, WorkflowActiveTokenInfo>(), TimeSpan.FromSeconds(1),
        new Dictionary<string, WorkflowNodeRuntimeInfo>
        {
            [parentNodeId] = new(parentNodeId, E_NodeState.Running, 1, 1, DateTimeOffset.UtcNow, null, TimeSpan.FromSeconds(1))
        },
        new Dictionary<long, WorkflowParallelScopeInfo>(),
        new Dictionary<string, WorkflowChildRuntimeInfo>
        {
            [$"{parentNodeId}:1:1"] = new(parentNodeId, new WorkflowExecutionIdentity(Guid.NewGuid(), 1, Array.Empty<long>(), Array.Empty<long>(), 1), child, cycle)
        },
        new Dictionary<string, WorkflowChildRuntimeInfo>(),
        Array.Empty<string>());

    private static WorkflowDocument Document(IWorkflowNodeModel node)
    {
        var canvasDocument = new WorkflowDocument { Name = "Editor" };
        var canvas = canvasDocument.CanvasProjection;
        canvas.Nodes.Add(new WorkflowCanvasNode { Node = node });
        return canvasDocument;
    }

    private sealed class PlainNode : WorkflowNodeModel
    {
        public override string NodeType => "Action";
    }

    private sealed class SlotProvider(string extensionId, string slotId, int priority) : IWorkflowNodeEditorPageProvider
    {
        public string ExtensionId => extensionId;
        public bool CanProvide(WorkflowNodeEditorContext context) => true;
        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                slotId, slotId, WorkflowNodeEditorPageKind.Custom, 500, context.Node,
                RendererKey: extensionId,
                Priority: priority);
        }
    }

    private sealed class MissingRendererKeyProvider : IWorkflowNodeEditorPageProvider
    {
        public string ExtensionId => nameof(MissingRendererKeyProvider);
        public bool CanProvide(WorkflowNodeEditorContext context) => true;

        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                "MissingRenderer", "缺少 Renderer", WorkflowNodeEditorPageKind.Custom, 500, context.Node);
        }
    }

    private sealed class CustomProvider : IWorkflowNodeEditorPageProvider
    {
        public string ExtensionId => nameof(CustomProvider);
        public bool CanProvide(WorkflowNodeEditorContext context) => true;
        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                "HostCustom", "扩展", WorkflowNodeEditorPageKind.Custom, 500,
                new DemoPageModel(context.Node.Id),
                RendererKey: "DemoRenderer");
        }
    }

    private sealed record DemoPageModel(string NodeId);
}

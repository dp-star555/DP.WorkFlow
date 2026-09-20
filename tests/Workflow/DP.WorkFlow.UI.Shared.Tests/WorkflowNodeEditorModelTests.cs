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

        Assert.Equal(new[] { "Properties", "Script", "ScriptDiagnostics" }, model.Pages.Select(page => page.PageId));
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
        public bool CanProvide(WorkflowNodeEditorContext context) => true;

        public IEnumerable<WorkflowNodeEditorPageDescriptor> CreatePages(WorkflowNodeEditorContext context)
        {
            yield return new WorkflowNodeEditorPageDescriptor(
                "MissingRenderer", "缺少 Renderer", WorkflowNodeEditorPageKind.Custom, 500, context.Node);
        }
    }

    private sealed class CustomProvider : IWorkflowNodeEditorPageProvider
    {
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
